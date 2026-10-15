// SPDX-License-Identifier: GPL-3.0-or-later
// S2N contributors. Independently implemented against SagerNet libbox v1.14.3.
package com.cavwic.s2n.vpn

import android.app.Notification
import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.PendingIntent
import android.content.Context
import android.content.Intent
import android.content.pm.ServiceInfo
import android.net.VpnService
import android.os.Build
import android.os.ParcelFileDescriptor
import io.nekohasekai.libbox.CommandServer
import io.nekohasekai.libbox.CommandServerHandler
import io.nekohasekai.libbox.Libbox
import io.nekohasekai.libbox.OverrideOptions
import io.nekohasekai.libbox.SetupOptions
import io.nekohasekai.libbox.SystemProxyStatus
import java.io.File
import java.util.concurrent.Executors
import java.util.concurrent.RejectedExecutionException
import java.util.concurrent.atomic.AtomicLong

/** Owns the real Android TUN and libbox. RUNNING does not assert Internet reachability. */
class S2NVpnService : VpnService(), CommandServerHandler {
    companion object {
        const val ACTION_START = "com.cavwic.s2n.vpn.START"
        const val ACTION_STOP = "com.cavwic.s2n.vpn.STOP"
        const val STATUS_CHANGED = "com.cavwic.s2n.vpn.STATUS_CHANGED"
        const val ACTION_STATUS_CHANGED = STATUS_CHANGED
        const val CONFIG_JSON = "CONFIG_JSON"
        const val EXTRA_CONFIG_JSON = CONFIG_JSON
        const val EXTRA_STATE = "state"
        const val EXTRA_MESSAGE = "message"
        const val STATE_STARTING = "STARTING"
        const val STATE_RUNNING = "RUNNING"
        const val STATE_STOPPED = "STOPPED"
        const val STATE_ERROR = "ERROR"

        @Volatile var currentState: String = STATE_STOPPED
            private set
        val lastState: String get() = currentState
        @Volatile var lastMessage: String = ""
            private set

        private const val CHANNEL_ID = "s2n-vpn"
        private const val NOTIFICATION_ID = 2101
        private var initialized = false

        /** Also usable by UI validation and instrumentation, without starting a VPN. */
        @JvmStatic
        @Synchronized
        fun initializeLibbox(context: Context) {
            if (initialized) return
            val app = context.applicationContext
            val base = File(app.noBackupFilesDir, "libbox").apply { mkdirs() }
            val working = File(base, "working").apply { mkdirs() }
            val temporary = File(app.cacheDir, "libbox").apply { mkdirs() }
            Libbox.setup(SetupOptions().apply {
                basePath = base.absolutePath
                workingPath = working.absolutePath
                tempPath = temporary.absolutePath
                fixAndroidStack = Build.VERSION.SDK_INT >= Build.VERSION_CODES.P
                logMaxLines = 100
                debug = false
                crashReportSource = "S2N"
                oomKillerEnabled = false
                oomKillerDisabled = true
                powerReportEnabled = false
            })
            initialized = true
        }

        @JvmStatic
        fun coreVersion(context: Context): String {
            initializeLibbox(context)
            return Libbox.version()
        }

        @JvmStatic
        fun checkConfig(context: Context, json: String) {
            initializeLibbox(context)
            Libbox.checkConfig(json)
        }
    }

    private val worker = Executors.newSingleThreadExecutor()
    private val generation = AtomicLong()
    private val tunLock = Any()
    @Volatile private var destroyed = false
    @Volatile private var currentStartId = 0
    @Volatile private var activeGeneration = 0L
    private var core: CommandServer? = null
    @Volatile private var platform: LibboxPlatform? = null
    private var tun: ParcelFileDescriptor? = null

    override fun onStartCommand(intent: Intent?, flags: Int, startId: Int): Int {
        currentStartId = startId
        val token = generation.incrementAndGet()
        if (intent?.action != ACTION_START) {
            enqueue { stop(token, startId) }
            return START_NOT_STICKY
        }

        val config = intent.getStringExtra(CONFIG_JSON).orEmpty()
        sendStatus(STATE_STARTING, text("正在启动 VPN", "Starting VPN"))
        try {
            showForeground(text("正在启动", "Starting"))
        } catch (_: Exception) {
            enqueue { fail(token, startId, "foreground") }
            return START_NOT_STICKY
        }
        enqueue { start(token, startId, config) }
        return START_NOT_STICKY
    }

    private fun start(token: Long, startId: Int, config: String) {
        if (!isCurrent(token)) return
        var stage = "cleanup"
        try {
            check(closeResources())
            stage = "permission"
            check(prepare(this) == null)
            stage = "configuration"
            require(config.isNotBlank())
            initializeLibbox(this)
            Libbox.checkConfig(config)
            if (!isCurrent(token)) return

            stage = "network"
            val adapter = LibboxPlatform(this)
            platform = adapter
            adapter.networkMonitor.start()
            check(adapter.networkMonitor.currentNetwork != null)
            if (!isCurrent(token)) {
                closeResources()
                return
            }

            stage = "core"
            activeGeneration = token
            val server = CommandServer(this, adapter)
            core = server
            server.start()
            server.startOrReloadService(config, OverrideOptions())
            if (!isCurrent(token)) {
                closeResources()
                return
            }
            stage = "tun"
            check(synchronized(tunLock) { tun != null })
            // Runtime service state is held by libbox; its crash-report snapshot
            // is not required for this client's explicit restart/reload policy.
            File(noBackupFilesDir, "libbox/configuration.json").delete()
            showForeground(text("VPN 已运行", "VPN running"))
            sendStatus(
                STATE_RUNNING,
                text("VPN 已运行，尚未验证外网连通性", "VPN running; Internet reachability not verified"),
            )
        } catch (_: Exception) {
            // Native parser/connection errors may contain UUIDs or configuration text.
            // Never publish their messages, causes, or stack traces.
            fail(token, startId, stage)
        }
    }

    internal fun attachTun(descriptor: ParcelFileDescriptor) {
        if (!isCurrent(activeGeneration)) {
            descriptor.close()
            error("VPN start was cancelled")
        }
        synchronized(tunLock) {
            check(tun == null) { "TUN already exists" }
            tun = descriptor
        }
        updateUnderlyingNetwork()
    }

    internal fun newTunBuilder(): Builder = Builder()

    internal fun updateUnderlyingNetwork() {
        val network = platform?.networkMonitor?.currentNetwork
        setUnderlyingNetworks(network?.let { arrayOf(it) })
    }

    private fun fail(token: Long, startId: Int, stage: String) {
        closeResources()
        if (!isCurrent(token)) return
        removeForeground()
        sendStatus(
            STATE_ERROR,
            text("VPN 启动失败，请检查配置、网络和系统授权", "VPN failed to start; check configuration, network, and permission") + " [$stage]",
        )
        stopSelfResult(startId)
    }

    private fun stop(token: Long, startId: Int) {
        if (!isCurrent(token)) return
        val closed = closeResources()
        // A newer start may have arrived while native shutdown was in progress.
        if (!isCurrent(token)) return
        removeForeground()
        sendStatus(
            if (closed) STATE_STOPPED else STATE_ERROR,
            if (closed) text("VPN 已停止", "VPN stopped")
            else text("VPN 清理失败，请重新启动应用", "VPN cleanup failed; restart the application"),
        )
        stopSelfResult(startId)
    }

    /** Always attempt every cleanup operation, even if an earlier one failed. */
    private fun closeResources(): Boolean {
        var success = true
        val server = core
        core = null
        if (server != null) {
            runCatching { server.closeService() }.onFailure { success = false }
            runCatching { server.close() }.onFailure { success = false }
        }
        val descriptor = synchronized(tunLock) { tun.also { tun = null } }
        runCatching { descriptor?.close() }.onFailure { success = false }
        val adapter = platform
        platform = null
        runCatching { adapter?.close() }.onFailure { success = false }
        runCatching { setUnderlyingNetworks(null) }.onFailure { success = false }
        // libbox writes its own snapshot; keep it private and remove it after use.
        File(noBackupFilesDir, "libbox/configuration.json").delete()
        return success
    }

    override fun onRevoke() {
        val token = generation.incrementAndGet()
        enqueue { stop(token, currentStartId) }
    }

    override fun onDestroy() {
        destroyed = true
        generation.incrementAndGet()
        enqueue {
            val closed = closeResources()
            removeForeground()
            if (lastState != STATE_ERROR) {
                sendStatus(
                    if (closed) STATE_STOPPED else STATE_ERROR,
                    if (closed) text("VPN 已停止", "VPN stopped")
                    else text("VPN 清理失败", "VPN cleanup failed"),
                )
            }
        }
        worker.shutdown()
        super.onDestroy()
    }

    private fun enqueue(action: () -> Unit) {
        try {
            worker.execute(action)
        } catch (_: RejectedExecutionException) {
            // onDestroy already queued the final cleanup before shutting down.
        }
    }

    private fun isCurrent(token: Long) = !destroyed && token == generation.get()

    private fun sendStatus(state: String, message: String) {
        lastMessage = message
        currentState = state
        sendBroadcast(Intent(STATUS_CHANGED).setPackage(packageName)
            .putExtra(EXTRA_STATE, state)
            .putExtra(EXTRA_MESSAGE, message))
    }

    private fun showForeground(message: String) {
        val manager = getSystemService(NotificationManager::class.java)
        manager.createNotificationChannel(NotificationChannel(
            CHANNEL_ID, "S2N VPN", NotificationManager.IMPORTANCE_LOW,
        ))
        val stopIntent = PendingIntent.getService(
            this, 0, Intent(this, S2NVpnService::class.java).setAction(ACTION_STOP),
            PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE,
        )
        val builder = Notification.Builder(this, CHANNEL_ID)
            .setSmallIcon(applicationInfo.icon)
            .setContentTitle("S2N")
            .setContentText(message)
            .setOngoing(true)
            .setOnlyAlertOnce(true)
            .setCategory(Notification.CATEGORY_SERVICE)
            .addAction(Notification.Action.Builder(
                null, text("停止", "Stop"), stopIntent,
            ).build())
        packageManager.getLaunchIntentForPackage(packageName)?.let {
            builder.setContentIntent(PendingIntent.getActivity(
                this, 1, it, PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE,
            ))
        }
        if (Build.VERSION.SDK_INT >= 34) {
            startForeground(NOTIFICATION_ID, builder.build(), ServiceInfo.FOREGROUND_SERVICE_TYPE_SYSTEM_EXEMPTED)
        } else {
            startForeground(NOTIFICATION_ID, builder.build())
        }
    }

    private fun removeForeground() {
        stopForeground(STOP_FOREGROUND_REMOVE)
    }

    private fun text(chinese: String, english: String): String =
        if (getSharedPreferences("ui", MODE_PRIVATE).getBoolean("english", false)) english else chinese

    // Optional command-server features are deliberately unavailable in this client.
    override fun serviceStop() {
        val token = generation.incrementAndGet()
        enqueue { stop(token, currentStartId) }
    }
    override fun serviceReload() = throw UnsupportedOperationException("Restart from S2N to apply configuration")
    override fun getSystemProxyStatus(): SystemProxyStatus = SystemProxyStatus().apply {
        available = false
        enabled = false
    }
    override fun setSystemProxyEnabled(enabled: Boolean) = throw UnsupportedOperationException("TUN only")
    override fun triggerNativeCrash() = throw UnsupportedOperationException("Debug commands disabled")
    override fun writeDebugMessage(message: String?) = Unit
    override fun connectSSHAgent(): Int = -1
}
