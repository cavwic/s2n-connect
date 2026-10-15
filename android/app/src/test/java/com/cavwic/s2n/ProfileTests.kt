package com.cavwic.s2n

import org.json.JSONObject
import org.junit.Assert.*
import org.junit.Test

class ProfileTests {
    private val example = Profile("192.0.2.10", 443, "11111111-1111-4111-8111-111111111111",
        "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA", "abcd", "www.example.com")
    @Test fun desktopJsonRoundTrip() {
        assertEquals(example, Profile.parse(example.toJson().put("updatedAt", "2026-01-01T00:00:00Z").toString()))
        val pascal = JSONObject()
        example.toJson().keys().forEach { key -> pascal.put(key.replaceFirstChar { it.uppercase() }, example.toJson().get(key)) }
        assertEquals(example, Profile.parse(pascal.toString()))
    }
    @Test fun vlessImport() {
        val uri = "vless://${example.uuid}@192.0.2.10:443?security=reality&type=tcp&flow=xtls-rprx-vision&pbk=${example.publicKey}&sid=abcd&sni=www.example.com#Demo"
        assertEquals(example, Profile.parse(uri))
    }
    @Test fun rejectsInvalidCredentialsAndInjection() {
        listOf(example.copy(serverIp = "example.com\ninvalid"), example.copy(serverPort = 0),
            example.copy(uuid = "1-1-1-1-1"), example.copy(publicKey = "bad"),
            example.copy(shortId = "a"), example.copy(shortId = "zz"), example.copy(serverName = "bad/sni"))
            .forEach { assertThrows(IllegalArgumentException::class.java) { it.validate() } }
        assertThrows(Exception::class.java) { Profile.parse("{\"sshPrivateKey\":\"NOT_A_REAL_KEY\"}") }
    }
    @Test fun emptyRulesNeverMatchAllTraffic() {
        val config = JSONObject(ConfigBuilder.build(example, false, emptyList()))
        val rules = config.getJSONObject("route").getJSONArray("rules")
        assertEquals("direct", config.getJSONObject("route").getString("final"))
        for (i in 0 until rules.length()) assertFalse(rules.getJSONObject(i).has("domain_suffix"))
    }
    @Test fun globalAndPrivateTrafficRoutes() {
        val config = JSONObject(ConfigBuilder.build(example, true, listOf("example.com")))
        assertEquals("vless-out", config.getJSONObject("route").getString("final"))
        val rules = config.getJSONObject("route").getJSONArray("rules")
        assertEquals("direct", rules.getJSONObject(2).getString("outbound"))
        assertEquals("vless-out", rules.getJSONObject(3).getString("outbound"))
        assertEquals(2, config.getJSONArray("inbounds").getJSONObject(1).getJSONArray("address").length())
        assertFalse(config.toString().contains("process_name"))
    }
    @Test fun domainNormalizationAndValidation() {
        assertEquals(listOf("google.com", "openai.com"), ConfigBuilder.domains(" GOOGLE.com\nopenai.com,google.com "))
        assertEquals(emptyList<String>(), ConfigBuilder.domains(" \n"))
        assertThrows(IllegalArgumentException::class.java) { ConfigBuilder.domains("https://example.com/path") }
    }
}
