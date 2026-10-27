package com.cavwic.s2n

import org.json.JSONObject
import org.junit.Assert.*
import org.junit.Test

class HysteriaProfileTests {
    private fun example() = Profile("192.0.2.10", 443, "", "", "", "s2n-test.example.invalid",
        "hysteria2", "SYNTHETIC +/:@PASSWORD")
    private fun certificate() = javaClass.getResourceAsStream("/hysteria-test-certificate.txt")!!
        .bufferedReader(Charsets.US_ASCII).use { it.readText() }

    @Test fun versionTwoJsonRoundTripsPasswordAndCertificate() {
        listOf(example(), example().copy(certificatePem = certificate())).forEach { original ->
            val json = original.toJson().put("updatedAt", "2026-10-27T00:00:00Z")
            assertEquals(2, json.getInt("schemaVersion"))
            assertEquals(original, Profile.parse(json.toString()))
            assertFalse(json.has("uuid")); assertFalse(json.has("publicKey")); assertFalse(json.has("shortId"))
        }
    }

    @Test fun pascalCaseMobileProfileRemainsCompatible() {
        val source = example().toJson()
        val pascal = JSONObject()
        source.keys().forEach { name -> pascal.put(name.replaceFirstChar { it.uppercase() }, source.get(name)) }
        assertEquals(example(), Profile.parse(pascal.toString()))
    }

    @Test fun udpOutboundEnforcesTlsAndKeepsDnsBootstrapOutsideProxy() {
        val config = JSONObject(ConfigBuilder.build(example(), true, listOf("example.com")))
        val outbound = config.getJSONArray("outbounds").getJSONObject(0)
        assertEquals("hysteria2", outbound.getString("type"))
        assertEquals(example().password, outbound.getString("password"))
        assertEquals("lan-dns", outbound.getString("domain_resolver"))
        assertFalse(outbound.has("uuid")); assertFalse(outbound.has("flow"))
        val tls = outbound.getJSONObject("tls")
        assertTrue(tls.getBoolean("enabled")); assertFalse(tls.getBoolean("insecure"))
        assertFalse(tls.has("certificate")); assertFalse(tls.has("reality")); assertFalse(tls.has("utls"))
        assertEquals("vless-out", config.getJSONObject("route").getString("final"))
        assertEquals("direct", config.getJSONObject("route").getJSONArray("rules").getJSONObject(2).getString("outbound"))
    }

    @Test fun trustedPublicCertificateSurvivesConfigBuild() {
        val pem = certificate()
        val config = JSONObject(ConfigBuilder.build(example().copy(certificatePem = pem), false, emptyList()))
        val tls = config.getJSONArray("outbounds").getJSONObject(0).getJSONObject("tls")
        val lines = tls.getJSONArray("certificate")
        assertEquals(pem.trim(), (0 until lines.length()).joinToString("\n") { lines.getString(it) })
        assertFalse(tls.getBoolean("insecure"))
    }

    @Test fun ipv6AndIpCertificateIdentityAreSupported() {
        val profile = example().copy(serverIp = "2001:db8::10", serverName = "2001:db8::10")
        assertEquals(profile, Profile.parse(profile.toJson().toString()))
        assertEquals("2001:db8::10", JSONObject(ConfigBuilder.build(profile, false, emptyList()))
            .getJSONArray("outbounds").getJSONObject(0).getString("server"))
    }

    @Test fun uriEscapesCredentialsWithoutWeakeningVerification() {
        val parsed = Profile.parse("hysteria2://SYNTHETIC%20%2B%2F%3A%40PASSWORD@192.0.2.10:443/?sni=s2n-test.example.invalid#S2N")
        assertEquals(example(), parsed)
        assertEquals("a+b", Profile.parse("hy2://a+b@192.0.2.10/?sni=s2n-test.example.invalid").password)
        assertEquals("2001:db8::10", Profile.parse("hy2://synthetic@[2001:db8::10]/?sni=s2n-test.example.invalid").serverIp)
        assertEquals(443, Profile.parse("hy2://synthetic@192.0.2.10/").serverPort)
    }

    @Test fun insecureAndUnsupportedTrustUrisAreRejected() {
        listOf("insecure=1", "insecure=true", "pinSHA256=abcd", "obfs=salamander", "sni=one.invalid&sni=two.invalid").forEach { query ->
            assertThrows(Exception::class.java) { Profile.parse("hysteria2://synthetic@192.0.2.10/?$query") }
        }
        assertThrows(Exception::class.java) { Profile.parse("hysteria2://synthetic@192.0.2.10:0/") }
    }

    @Test fun unknownSchemasAndProtocolsCannotFallbackToVless() {
        listOf(example().toJson().put("schemaVersion", 3), example().toJson().put("schemaVersion", 1),
            example().toJson().put("transport", "future"), example().toJson().put("serverPort", 443.1),
            example().toJson().put("password", JSONObject().put("secret", "not-a-string"))).forEach { value ->
            assertThrows(Exception::class.java) { Profile.parse(value.toString()) }
        }
    }

    @Test fun malformedCertificatesAndPasswordsAreRejectedWithoutCredentialText() {
        listOf(example().copy(password = ""), example().copy(password = "SYNTHETIC\nSECRET"),
            example().copy(certificatePem = "not-a-certificate"),
            example().copy(certificatePem = "-----BEGIN CERTIFICATE-----\nAAAA\n-----END CERTIFICATE-----"),
            example().copy(certificatePem = certificate() + "-----BEGIN PRIVATE KEY-----\nSYNTHETIC\n-----END PRIVATE KEY-----"))
            .forEach { value ->
                val error = assertThrows(IllegalArgumentException::class.java) { value.validate() }
                assertFalse(error.message.orEmpty().contains("SYNTHETIC"))
            }
    }

    @Test fun ignoredSshAndRawConfigFieldsNeverReachClientExport() {
        val input = example().toJson().put("sshKeyPath", "SYNTHETIC_PATH")
            .put("privateKey", "SYNTHETIC_PRIVATE").put("inbounds", JSONObject().put("listen", "0.0.0.0"))
        val output = Profile.parse(input.toString()).toJson().toString()
        assertFalse(output.contains("SYNTHETIC_PATH")); assertFalse(output.contains("SYNTHETIC_PRIVATE"))
        assertFalse(output.contains("inbounds"))
    }
}
