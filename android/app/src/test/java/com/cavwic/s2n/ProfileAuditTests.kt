package com.cavwic.s2n

import org.json.JSONObject
import org.junit.Assert.*
import org.junit.Test

class ProfileAuditTests {
    private fun fixture() = Profile("203.0.113.10", 443,
        "12345678-1234-4234-8234-123456789abc", "A".repeat(43), "1234abcd", "www.example.invalid")

    private fun uri(port: String) = "vless://${fixture().uuid}@vps.example.invalid$port" +
        "?security=reality&type=tcp&flow=xtls-rprx-vision&pbk=${fixture().publicKey}&sid=1234abcd&sni=www.example.invalid"

    @Test(expected = IllegalArgumentException::class)
    fun explicitZeroUriPortIsRejected() {
        Profile.parse(uri(":0"))
    }

    @Test(expected = IllegalArgumentException::class)
    fun oversizedUriPortIsRejected() {
        Profile.parse(uri(":65536"))
    }

    @Test
    fun omittedUriPortDefaultsTo443() {
        assertEquals(443, Profile.parse(uri("")).serverPort)
    }

    @Test
    fun validExplicitUriPortIsPreserved() {
        assertEquals(8443, Profile.parse(uri(":8443")).serverPort)
    }

    @Test
    fun desktopExportContractImportsAndIgnoresExtraFields() {
        val input = fixture().toJson().put("updatedAt", "2026-10-15T00:00:00+00:00")
            .put("sshKeyPath", "SYNTHETIC_IGNORED_PATH")
            .put("privateKey", "SYNTHETIC_IGNORED_PRIVATE_VALUE")
        val parsed = Profile.parse(input.toString())
        assertEquals(fixture(), parsed)
        assertEquals(6, parsed.toJson().length())
        assertFalse(parsed.toJson().has("sshKeyPath"))
        assertFalse(parsed.toJson().has("privateKey"))
    }

    @Test
    fun desktopPascalCaseProfilesRemainCompatible() {
        val source = fixture().toJson()
        val pascalCase = JSONObject()
        source.keys().forEach { name ->
            pascalCase.put(name.replaceFirstChar { it.uppercase() }, source.get(name))
        }
        assertEquals(fixture(), Profile.parse(pascalCase.toString()))
    }

    @Test
    fun emptyRealityShortIdIsPreserved() {
        val profile = fixture().copy(shortId = "")
        assertEquals(profile, Profile.parse(profile.toJson().toString()))
    }

    @Test
    fun directPublicDomainsUseRemoteDnsAndServerBootstrapUsesLocalDns() {
        val profile = fixture().copy(serverIp = "vps.example.invalid")
        val config = JSONObject(ConfigBuilder.build(profile, false, emptyList()))
        assertEquals("remote-doh", config.getJSONObject("route")
            .getJSONObject("default_domain_resolver").getString("server"))
        assertEquals("lan-dns", config.getJSONArray("outbounds").getJSONObject(0)
            .getString("domain_resolver"))
        assertEquals("remote-doh", config.getJSONObject("dns").getString("final"))
        assertEquals("vless-out", config.getJSONObject("dns").getJSONArray("servers")
            .getJSONObject(1).getString("detour"))
        assertEquals("direct", config.getJSONObject("route").getString("final"))
    }
}
