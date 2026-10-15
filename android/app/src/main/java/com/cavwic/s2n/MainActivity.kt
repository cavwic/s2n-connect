package com.cavwic.s2n

import android.Manifest
import android.app.Activity
import android.app.AlertDialog
import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent
import android.content.IntentFilter
import android.content.res.Configuration
import android.graphics.Color
import android.net.VpnService
import android.os.Build
import android.os.Bundle
import android.text.InputType
import android.view.Gravity
import android.view.View
import android.view.WindowManager
import android.widget.*
import com.cavwic.s2n.vpn.S2NVpnService
import java.net.HttpURLConnection
import java.net.InetSocketAddress
import java.net.Proxy
import java.net.URL
import java.util.concurrent.Executors

class MainActivity : Activity() {
    private val uiPrefs by lazy { getSharedPreferences("ui", MODE_PRIVATE) }
    private val store by lazy { SecureStore(this) }
    private val executor = Executors.newSingleThreadExecutor()
    private var english = false
    private var dark = false
    private lateinit var layout: LinearLayout
    private lateinit var status: TextView
    private lateinit var connect: Button
    private lateinit var stop: Button
    private lateinit var paste: EditText
    private lateinit var server: EditText
    private lateinit var port: EditText
    private lateinit var uuid: EditText
    private lateinit var key: EditText
    private lateinit var shortId: EditText
    private lateinit var sni: EditText
    private lateinit var domainRules: EditText
    private lateinit var global: Switch
    private var pendingConfig: String? = null
    private var receiverRegistered = false
    private val receiver = object : BroadcastReceiver() {
        override fun onReceive(context: Context?, intent: Intent?) { updateStatus() }
    }

    override fun onCreate(savedInstanceState: Bundle?) {
        english = uiPrefs.getBoolean("english", false)
        dark = when (uiPrefs.getString("theme", "system")) {
            "dark" -> true
            "light" -> false
            else -> resources.configuration.uiMode and Configuration.UI_MODE_NIGHT_MASK == Configuration.UI_MODE_NIGHT_YES
        }
        setTheme(if (dark) android.R.style.Theme_Material_NoActionBar else android.R.style.Theme_Material_Light_NoActionBar)
        super.onCreate(savedInstanceState)
        title = "S2N Connect"
        if (!BuildConfig.DEBUG) window.addFlags(WindowManager.LayoutParams.FLAG_SECURE)
        if (Build.VERSION.SDK_INT >= 30) {
            val appearance = android.view.WindowInsetsController.APPEARANCE_LIGHT_STATUS_BARS or
                android.view.WindowInsetsController.APPEARANCE_LIGHT_NAVIGATION_BARS
            window.insetsController?.setSystemBarsAppearance(if (dark) 0 else appearance, appearance)
        } else {
            @Suppress("DEPRECATION")
            window.decorView.systemUiVisibility = if (dark) 0 else View.SYSTEM_UI_FLAG_LIGHT_STATUS_BAR or View.SYSTEM_UI_FLAG_LIGHT_NAVIGATION_BAR
        }
        buildUi()
    }

    private fun t(zh: String, en: String) = if (english) en else zh
    private fun dp(value: Int) = (value * resources.displayMetrics.density).toInt()
    private fun buildUi() {
        val scroll = ScrollView(this).apply { isFillViewport = true }
        layout = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            setPadding(dp(20), dp(12), dp(20), dp(24))
            setBackgroundColor(if (dark) Color.rgb(15, 24, 38) else Color.rgb(246, 249, 252))
        }
        scroll.addView(layout)
        setContentView(scroll)
        scroll.setOnApplyWindowInsetsListener { view, insets ->
            @Suppress("DEPRECATION")
            view.setPadding(insets.systemWindowInsetLeft, insets.systemWindowInsetTop,
                insets.systemWindowInsetRight, insets.systemWindowInsetBottom)
            insets
        }
        val header = LinearLayout(this).apply { gravity = Gravity.CENTER_VERTICAL }
        header.addView(ImageView(this).apply { setImageResource(R.mipmap.ic_launcher); contentDescription = "S2N" },
            LinearLayout.LayoutParams(dp(48), dp(48)))
        header.addView(TextView(this).apply { text = "S2N Connect"; textSize = 25f; setPadding(dp(12), 0, 0, 0) },
            LinearLayout.LayoutParams(0, dp(60), 1f))
        header.addView(Button(this).apply {
            id = R.id.language_button; text = "文/A"; textSize = 16f
            contentDescription = t("切换为英文", "Switch to Chinese")
            setOnClickListener { persistQuietly(); uiPrefs.edit().putBoolean("english", !english).apply(); recreate() }
        }, LinearLayout.LayoutParams(dp(66), dp(52)))
        header.addView(Button(this).apply {
            id = R.id.theme_button; text = "◐"; contentDescription = t("主题", "Theme")
            setOnClickListener {
                AlertDialog.Builder(this@MainActivity).setTitle(t("主题", "Theme"))
                    .setItems(arrayOf(t("跟随系统", "System"), t("浅色", "Light"), t("深色", "Dark"))) { _, which ->
                        persistQuietly()
                        uiPrefs.edit().putString("theme", arrayOf("system", "light", "dark")[which]).apply(); recreate()
                    }.show()
            }
        }, LinearLayout.LayoutParams(dp(54), dp(52)))
        layout.addView(header)
        label(t("为 VPS 增加 VPN 功能", "Add VPN functionality to your VPS"), 18f)
        label("${BuildConfig.VERSION_NAME} · Android 8+ · VLESS + REALITY", 14f)
        status = label("", 19f).apply { id = R.id.status_text }
        label(t("导入配置", "Import profile"), 21f)
        label(t("在桌面端部署 VPS 后导出手机配置，或粘贴 VLESS + REALITY 链接。", "Export a mobile profile after VPS deployment on desktop, or paste a VLESS + REALITY link."))
        paste = field(t("粘贴 JSON 或 vless:// 链接", "Paste JSON or vless:// link"), "", true).apply {
            id = R.id.profile_import; maxLines = 5
        }
        button(t("导入粘贴内容", "Import pasted content"), R.id.import_button) { importText(paste.text.toString()) }
        button(t("选择配置文件", "Choose profile file")) {
            startActivityForResult(Intent(Intent.ACTION_OPEN_DOCUMENT).apply {
                addCategory(Intent.CATEGORY_OPENABLE); type = "*/*"
            }, REQUEST_FILE)
        }
        label(t("连接参数", "Connection parameters"), 21f)
        server = field(t("VPS 地址", "VPS address"))
        port = field(t("端口", "Port"), "443").apply { inputType = InputType.TYPE_CLASS_NUMBER }
        uuid = field("UUID")
        key = field(t("REALITY 公钥", "REALITY public key"))
        shortId = field("Short ID")
        sni = field("SNI")
        global = Switch(this).apply { text = t("全局代理（关闭后按域名规则）", "Global proxy (off: domain rules)"); textSize = 17f }
        layout.addView(global, LinearLayout.LayoutParams(-1, dp(64)))
        domainRules = field(t("代理域名，每行一个", "Proxy domains, one per line"), ConfigBuilder.DEFAULT_DOMAINS, true)
        label(t("规则模式：所列域名走 VPS，其余直连；全局模式仍保持局域网直连。", "Rule mode: listed domains use the VPS; other traffic connects directly. LAN stays direct in global mode."))
        button(t("保存配置", "Save profile")) {
            runCatching { persist() }.fold({ show(t("已加密保存", "Saved with encryption")) }, { invalid() })
        }
        connect = button(t("连接 VPN", "Connect VPN"), R.id.connect_button) { prepareVpn() }
        stop = button(t("停止 VPN", "Stop VPN"), R.id.stop_button) {
            startService(Intent(this, S2NVpnService::class.java).setAction(S2NVpnService.ACTION_STOP))
        }
        button(t("验证代理访问", "Verify proxy access")) { verifyProxy() }
        label(t("启动表示 VPN 接口已建立；请用“验证代理访问”检查 VPS 是否可达。", "Started means the VPN interface is running. Use Verify proxy access to check the VPS connection."))
        label(t("配置含连接凭据，请妥善保管。应用默认不自动连接。", "Profiles contain connection credentials. Keep them private. The app does not connect automatically."))
        button(t("开源许可", "Open source licenses")) {
            val licenses = assets.open("OPEN_SOURCE_LICENSES.txt").bufferedReader().use { it.readText() }
            AlertDialog.Builder(this).setTitle(t("开源许可", "Open source licenses"))
                .setMessage(licenses).setPositiveButton(android.R.string.ok, null).show()
        }
        runCatching { store.load() }.onSuccess { data ->
            if (data != null) {
                fill(Profile.parse(data.getJSONObject("profile").toString()))
                global.isChecked = data.optBoolean("global")
                domainRules.setText(data.optString("domains", ConfigBuilder.DEFAULT_DOMAINS))
            }
        }.onFailure { show(t("保存的配置无法解密，请重新导入。", "The saved profile cannot be decrypted. Import it again.")) }
        updateStatus()
    }

    private fun label(value: String, size: Float = 16f): TextView = TextView(this).apply {
        text = value; textSize = size; setPadding(0, dp(8), 0, dp(8))
        this@MainActivity.layout.addView(this, LinearLayout.LayoutParams(-1, -2))
    }
    private fun field(hintText: String, value: String = "", multiline: Boolean = false): EditText = EditText(this).apply {
        hint = hintText; textSize = 17f; setText(value); setSingleLine(!multiline)
        inputType = InputType.TYPE_CLASS_TEXT or InputType.TYPE_TEXT_FLAG_NO_SUGGESTIONS or
            (if (multiline) InputType.TYPE_TEXT_FLAG_MULTI_LINE else 0)
        importantForAutofill = View.IMPORTANT_FOR_AUTOFILL_NO
        this@MainActivity.layout.addView(this, LinearLayout.LayoutParams(-1, -2).apply { bottomMargin = dp(6) })
    }
    private fun button(value: String, viewId: Int = View.NO_ID, action: () -> Unit): Button = Button(this).apply {
        id = viewId; text = value; textSize = 17f; isAllCaps = false; setOnClickListener { action() }
        this@MainActivity.layout.addView(this, LinearLayout.LayoutParams(-1, dp(56)).apply { topMargin = dp(4) })
    }
    private fun fill(profile: Profile) {
        server.setText(profile.serverIp); port.setText(profile.serverPort.toString()); uuid.setText(profile.uuid)
        key.setText(profile.publicKey); shortId.setText(profile.shortId); sni.setText(profile.serverName)
    }
    private fun profile() = Profile(server.text.toString().trim(), port.text.toString().toIntOrNull() ?: 0,
        uuid.text.toString().trim(), key.text.toString().trim(), shortId.text.toString().trim(), sni.text.toString().trim()).validate()
    private fun persist(): Profile {
        val profile = profile()
        ConfigBuilder.domains(domainRules.text.toString())
        store.save(profile, global.isChecked, domainRules.text.toString())
        return profile
    }
    private fun persistQuietly() { runCatching { persist() } }
    private fun importText(text: String) {
        runCatching { Profile.parse(text) }.fold({
            fill(it); paste.text.clear(); show(t("已导入，请保存或连接。", "Imported. Save or connect to continue."))
        }, { invalid() })
    }
    private fun invalid() = show(t("配置无效，请检查地址、端口、UUID、公钥、Short ID、SNI 和域名规则。", "Invalid profile. Check address, port, UUID, public key, Short ID, SNI and domain rules."))
    private fun show(text: String) { Toast.makeText(this, text, Toast.LENGTH_LONG).show() }
    private fun prepareVpn() {
        val config = runCatching { ConfigBuilder.build(persist(), global.isChecked, ConfigBuilder.domains(domainRules.text.toString())) }
            .getOrElse { invalid(); return }
        pendingConfig = config
        val authorization = VpnService.prepare(this)
        if (authorization == null) startVpn() else startActivityForResult(authorization, REQUEST_VPN)
    }
    private fun startVpn() {
        val config = pendingConfig ?: return
        pendingConfig = null
        if (Build.VERSION.SDK_INT >= 33 && checkSelfPermission(Manifest.permission.POST_NOTIFICATIONS) != android.content.pm.PackageManager.PERMISSION_GRANTED)
            requestPermissions(arrayOf(Manifest.permission.POST_NOTIFICATIONS), REQUEST_NOTIFICATION)
        startForegroundService(Intent(this, S2NVpnService::class.java).setAction(S2NVpnService.ACTION_START)
            .putExtra(S2NVpnService.CONFIG_JSON, config))
    }
    @Deprecated("Android Activity result compatibility")
    override fun onActivityResult(requestCode: Int, resultCode: Int, data: Intent?) {
        super.onActivityResult(requestCode, resultCode, data)
        when (requestCode) {
            REQUEST_VPN -> if (resultCode == RESULT_OK) startVpn() else {
                pendingConfig = null; show(t("已取消 VPN 授权", "VPN authorization cancelled")); updateStatus()
            }
            REQUEST_FILE -> if (resultCode == RESULT_OK) data?.data?.let { uri ->
                runCatching {
                    contentResolver.openInputStream(uri)?.use { stream ->
                        val buffer = ByteArray(65537)
                        var count = 0
                        while (count < buffer.size) {
                            val read = stream.read(buffer, count, buffer.size - count)
                            if (read < 0) break
                            count += read
                        }
                        require(count <= 65536)
                        String(buffer, 0, count, Charsets.UTF_8)
                    } ?: error("file")
                }.fold({ importText(it) }, { invalid() })
            }
        }
    }
    private fun updateStatus() {
        val state = S2NVpnService.currentState
        status.text = when (state) {
            "STARTING" -> t("正在启动…", "Starting…")
            "RUNNING" -> t("VPN 接口已启动", "VPN interface running")
            "ERROR" -> t("启动失败，请检查配置或重新授权。", "Start failed. Check the profile or authorize again.")
            else -> t("未连接", "Disconnected")
        }
        connect.isEnabled = state != "STARTING" && state != "RUNNING"
        stop.isEnabled = state == "STARTING" || state == "RUNNING"
    }
    private fun verifyProxy() {
        if (S2NVpnService.currentState != "RUNNING") { show(t("请先连接 VPN", "Connect VPN first")); return }
        show(t("正在验证…", "Verifying…"))
        executor.execute {
            val ok = runCatching {
                val connection = URL("https://www.gstatic.com/generate_204").openConnection(
                    Proxy(Proxy.Type.HTTP, InetSocketAddress("127.0.0.1", 2080))) as HttpURLConnection
                try { connection.connectTimeout = 12000; connection.readTimeout = 12000; connection.responseCode == 204 }
                finally { connection.disconnect() }
            }.getOrDefault(false)
            runOnUiThread { if (!isFinishing) show(if (ok) t("代理访问验证通过", "Proxy access verified")
                else t("代理访问失败，请检查 VPS 和网络。", "Proxy access failed. Check the VPS and network.")) }
        }
    }
    override fun onResume() {
        super.onResume()
        val filter = IntentFilter(S2NVpnService.STATUS_CHANGED)
        if (Build.VERSION.SDK_INT >= 33) registerReceiver(receiver, filter, RECEIVER_NOT_EXPORTED)
        else @Suppress("DEPRECATION") registerReceiver(receiver, filter)
        receiverRegistered = true
        updateStatus()
    }
    override fun onPause() {
        if (receiverRegistered) { unregisterReceiver(receiver); receiverRegistered = false }
        super.onPause()
    }
    override fun onDestroy() { executor.shutdownNow(); super.onDestroy() }
    companion object {
        private const val REQUEST_VPN = 1
        private const val REQUEST_FILE = 2
        private const val REQUEST_NOTIFICATION = 3
    }
}
