package com.cavwic.s2n

import android.app.Activity
import android.content.Intent
import android.net.ConnectivityManager
import android.net.NetworkCapabilities
import android.net.VpnService
import android.os.SystemClock
import androidx.test.core.app.ActivityScenario
import androidx.test.ext.junit.runners.AndroidJUnit4
import androidx.test.platform.app.InstrumentationRegistry
import androidx.test.uiautomator.By
import androidx.test.uiautomator.BySelector
import androidx.test.uiautomator.UiDevice
import androidx.test.uiautomator.UiObject2
import androidx.test.uiautomator.Until
import com.cavwic.s2n.vpn.S2NVpnService
import java.io.File
import org.junit.Assert.*
import org.junit.Test
import org.junit.runner.RunWith

/** No real VPS credentials, deployment, or outbound proxy traffic is used in these tests. */
@RunWith(AndroidJUnit4::class)
class VpnSmokeTests {
    private val instrumentation = InstrumentationRegistry.getInstrumentation()
    private val context = instrumentation.targetContext
    private val device = UiDevice.getInstance(instrumentation)
    private val example = Profile("192.0.2.10", 443, "11111111-1111-4111-8111-111111111111",
        "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA", "abcd", "www.example.com")

    @Test fun nativeCoreAcceptsProductionConfiguration() {
        assertTrue(S2NVpnService.coreVersion(context).startsWith("1.14.3"))
        S2NVpnService.checkConfig(context, ConfigBuilder.build(example, false, emptyList()))
        S2NVpnService.checkConfig(context, ConfigBuilder.build(example, true, listOf("example.com")))
    }

    @Test fun languageThemeAndProfileImportScreens() {
        context.getSharedPreferences("ui", 0).edit().clear().commit()
        ActivityScenario.launch(MainActivity::class.java).use { scenario ->
            instrumentation.waitForIdleSync()
            dismissPixelLauncherAnr()
            screenshot("01-chinese-system.png")
            scenario.onActivity { activity ->
                assertEquals("S2N Connect", activity.title)
                activity.findViewById<android.widget.EditText>(R.id.profile_import).setText(example.toJson().toString())
                activity.findViewById<android.widget.Button>(R.id.import_button).performClick()
                assertTrue(activity.findViewById<android.widget.EditText>(R.id.profile_import).text.isEmpty())
                activity.findViewById<android.widget.Button>(R.id.language_button).performClick()
            }
            instrumentation.waitForIdleSync()
            scenario.onActivity { activity ->
                assertEquals("Connect VPN", activity.findViewById<android.widget.Button>(R.id.connect_button).text.toString())
                activity.findViewById<android.widget.Button>(R.id.theme_button).performClick()
            }
            val dark = waitForUiTarget(By.text("Dark"), 5000)
            assertNotNull(dark)
            dark!!.click()
            instrumentation.waitForIdleSync()
            screenshot("02-english-dark.png")
        }
    }

    private fun screenshot(name: String) {
        val output = InstrumentationRegistry.getArguments().getString("additionalTestOutputDir")
            ?.takeIf { it.isNotBlank() }?.let { File(it) } ?: context.getExternalFilesDir(null)
        val directory = File(output, "screenshots").apply { mkdirs() }
        assertTrue("UI screenshot must be written", device.takeScreenshot(File(directory, name)))
    }

    private fun dismissPixelLauncherAnr() {
        // The CI image can display a Launcher ANR over an otherwise working app.
        // Match this observed system dialog only; app crashes and permission dialogs
        // must remain visible and fail their original assertions.
        val message = By.pkg("android").text("Pixel Launcher isn't responding")
        device.findObject(message) ?: return
        screenshot("00-launcher-anr.png")
        val close = device.findObject(By.pkg("android").text("Close app"))
        assertNotNull("The identified Pixel Launcher ANR must offer Close app", close)
        close!!.click()
        assertTrue("Pixel Launcher ANR must disappear after Close app",
            device.wait(Until.gone(message), 5000) == true)
    }

    private fun waitForUiTarget(selector: BySelector, timeout: Long): UiObject2? {
        val deadline = SystemClock.elapsedRealtime() + timeout
        do {
            dismissPixelLauncherAnr()
            device.findObject(selector)?.let { return it }
            SystemClock.sleep(100)
        } while (SystemClock.elapsedRealtime() < deadline)
        return null
    }

    @Test fun authorizedSyntheticTunStartsAndStops() {
        ActivityScenario.launch(MainActivity::class.java).use { scenario ->
            dismissPixelLauncherAnr()
            // ConfirmDialog requires a calling Activity identity supplied by startActivityForResult.
            // Keep the app visible, matching a real user starting a VPN from the UI.
            val authorization = VpnService.prepare(context)
            if (authorization != null) {
                scenario.onActivity { activity -> activity.startActivityForResult(authorization, 901) }
                val ok = waitForUiTarget(By.res("android", "button1"), 15000)
                if (ok == null) screenshot("03-missing-vpn-authorization.png")
                assertNotNull("System VPN authorization dialog must be available", ok)
                ok!!.click()
                waitFor(10000) { VpnService.prepare(context) == null }
            }
            // 901 intentionally avoids MainActivity's user-connection request code:
            // only the direct fixture below may start, never the imported example profile.
            assertSyntheticTun()
        }
    }

    private fun assertSyntheticTun() {
        // Direct-only fixture exercises native libbox + VpnService TUN, without contacting a VPS.
        val fixture = """{"log":{"level":"fatal"},"inbounds":[{"type":"tun","tag":"test-tun","address":["172.19.0.1/30","fdfe:dcba:9876::1/126"],"auto_route":true,"stack":"mixed"}],"outbounds":[{"type":"direct","tag":"direct"}],"route":{"auto_detect_interface":true,"final":"direct"}}"""
        val manager = context.getSystemService(ConnectivityManager::class.java)
        fun vpnNetworks() = manager.allNetworks.filter {
            manager.getNetworkCapabilities(it)?.hasTransport(NetworkCapabilities.TRANSPORT_VPN) == true
        }.toSet()
        val existingVpnNetworks = vpnNetworks()
        try {
            context.startForegroundService(Intent(context, S2NVpnService::class.java)
                .setAction(S2NVpnService.ACTION_START).putExtra(S2NVpnService.CONFIG_JSON, fixture))
            waitFor(20000) { S2NVpnService.currentState == "RUNNING" || S2NVpnService.currentState == "ERROR" }
            assertEquals(S2NVpnService.lastMessage, "RUNNING", S2NVpnService.currentState)
            waitFor(10000) { (vpnNetworks() - existingVpnNetworks).isNotEmpty() }
        } finally {
            context.startService(Intent(context, S2NVpnService::class.java).setAction(S2NVpnService.ACTION_STOP))
            waitFor(15000) { S2NVpnService.currentState == "STOPPED" }
            waitFor(10000) { (vpnNetworks() - existingVpnNetworks).isEmpty() }
        }
    }

    private fun waitFor(timeout: Long, predicate: () -> Boolean) {
        val deadline = SystemClock.elapsedRealtime() + timeout
        while (!predicate() && SystemClock.elapsedRealtime() < deadline) SystemClock.sleep(100)
        assertTrue("Condition was not reached within ${timeout}ms (state=${S2NVpnService.currentState})", predicate())
    }
}
