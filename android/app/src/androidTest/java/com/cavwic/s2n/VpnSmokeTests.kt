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
import androidx.test.uiautomator.UiDevice
import androidx.test.uiautomator.Until
import com.cavwic.s2n.vpn.S2NVpnService
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
            assertNotNull(device.wait(Until.findObject(By.text("Dark")), 5000))
            device.findObject(By.text("Dark")).click()
            instrumentation.waitForIdleSync()
        }
    }

    @Test fun authorizedSyntheticTunStartsAndStops() {
        // Grant only this test emulator's VPN permission through the real Android authorization dialog.
        val authorization = VpnService.prepare(context)
        if (authorization != null) {
            context.startActivity(authorization.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK))
            val ok = device.wait(Until.findObject(By.res("android", "button1")), 10000)
            assertNotNull("System VPN authorization dialog must be available", ok)
            ok.click()
            waitFor(10000) { VpnService.prepare(context) == null }
        }
        // Direct-only fixture exercises native libbox + VpnService TUN, without contacting a VPS.
        val fixture = """{"log":{"level":"fatal"},"inbounds":[{"type":"tun","tag":"test-tun","address":["172.19.0.1/30","fdfe:dcba:9876::1/126"],"auto_route":true,"stack":"mixed"}],"outbounds":[{"type":"direct","tag":"direct"}],"route":{"auto_detect_interface":true,"final":"direct"}}"""
        try {
            context.startForegroundService(Intent(context, S2NVpnService::class.java)
                .setAction(S2NVpnService.ACTION_START).putExtra(S2NVpnService.CONFIG_JSON, fixture))
            waitFor(20000) { S2NVpnService.currentState == "RUNNING" || S2NVpnService.currentState == "ERROR" }
            assertEquals("RUNNING", S2NVpnService.currentState)
            val manager = context.getSystemService(ConnectivityManager::class.java)
            waitFor(10000) { manager.allNetworks.any { manager.getNetworkCapabilities(it)?.hasTransport(NetworkCapabilities.TRANSPORT_VPN) == true } }
        } finally {
            context.startService(Intent(context, S2NVpnService::class.java).setAction(S2NVpnService.ACTION_STOP))
            waitFor(15000) { S2NVpnService.currentState == "STOPPED" }
        }
    }

    private fun waitFor(timeout: Long, predicate: () -> Boolean) {
        val deadline = SystemClock.elapsedRealtime() + timeout
        while (!predicate() && SystemClock.elapsedRealtime() < deadline) SystemClock.sleep(100)
        assertTrue("Condition was not reached within ${timeout}ms (state=${S2NVpnService.currentState})", predicate())
    }
}
