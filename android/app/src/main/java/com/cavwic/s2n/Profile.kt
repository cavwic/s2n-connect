package com.cavwic.s2n

import org.json.JSONArray
import org.json.JSONObject
import java.net.IDN
import java.net.URI
import java.net.URLDecoder
import java.util.Base64
import java.util.UUID

/** Client connection credentials only. Never stores an SSH key or VPS private key. */
data class Profile(
    val serverIp: String, val serverPort: Int, val uuid: String,
    val publicKey: String, val shortId: String, val serverName: String
) {
    fun validate(): Profile {
        require(validHost(serverIp)) { "server" }
        require(serverPort in 1..65535) { "port" }
        require(runCatching { UUID.fromString(uuid).toString().equals(uuid, true) }.getOrDefault(false)) { "uuid" }
        require(Regex("[A-Za-z0-9_-]{43}").matches(publicKey) &&
            runCatching { Base64.getUrlDecoder().decode(publicKey).size == 32 }.getOrDefault(false)) { "public key" }
        require(shortId.length <= 16 && shortId.length % 2 == 0 && Regex("[0-9a-fA-F]*").matches(shortId)) { "short id" }
        require(validDomain(serverName)) { "SNI" }
        return this
    }

    fun toJson() = JSONObject().put("serverIp", serverIp).put("serverPort", serverPort)
        .put("uuid", uuid).put("publicKey", publicKey).put("shortId", shortId).put("serverName", serverName)

    companion object {
        fun parse(text: String): Profile {
            require(text.length <= 64 * 1024) { "size" }
            val input = text.trim()
            if (input.startsWith("vless://", true)) return fromUri(input).validate()
            val json = JSONObject(input)
            val keys = json.keys().asSequence().associateBy { it.lowercase() }
            fun str(key: String) = json.optString(keys[key.lowercase()] ?: key).trim()
            return Profile(str("serverIp"), str("serverPort").toIntOrNull() ?: 0,
                str("uuid"), str("publicKey"), str("shortId"), str("serverName")).validate()
        }

        private fun fromUri(text: String): Profile {
            val uri = URI(text)
            fun decode(value: String) = URLDecoder.decode(value, "UTF-8")
            val args = uri.rawQuery.orEmpty().split('&').filter { it.isNotEmpty() }.associate {
                val parts = it.split('=', limit = 2)
                decode(parts[0]) to decode(parts.getOrElse(1) { "" })
            }
            require(args["security"] == "reality") { "security" }
            require(args["type"].orEmpty() in listOf("", "tcp", "raw")) { "transport" }
            require(args["flow"].orEmpty() in listOf("", "xtls-rprx-vision")) { "flow" }
            require(uri.rawUserInfo != null && !uri.rawUserInfo.contains(':')) { "uuid" }
            require(uri.port == -1 || uri.port in 1..65535) { "port" }
            return Profile(uri.host.orEmpty().removeSurrounding("[", "]"), if (uri.port == -1) 443 else uri.port,
                decode(uri.rawUserInfo), args["pbk"].orEmpty(), args["sid"].orEmpty(), args["sni"].orEmpty())
        }

        fun validDomain(value: String): Boolean = runCatching {
            val ascii = IDN.toASCII(value, IDN.USE_STD3_ASCII_RULES)
            ascii.length in 1..253 && ascii.contains('.') && !ascii.endsWith('.') &&
                ascii.split('.').all { it.length in 1..63 && Regex("[A-Za-z0-9](?:[A-Za-z0-9-]*[A-Za-z0-9])?").matches(it) }
        }.getOrDefault(false)

        private fun validHost(value: String): Boolean {
            if (value.contains(':')) return Regex("[0-9a-fA-F:]+").matches(value) &&
                runCatching { URI("http://[$value]").host != null }.getOrDefault(false)
            if (Regex("[0-9.]+").matches(value)) return value.split('.').let { parts ->
                parts.size == 4 && parts.all { it.toIntOrNull() in 0..255 && it.length <= 3 }
            }
            return validDomain(value)
        }
    }
}

object ConfigBuilder {
    const val DEFAULT_DOMAINS = "google.com\ngoogleapis.com\ngstatic.com\ngoogleusercontent.com\n" +
        "youtube.com\ngooglevideo.com\nytimg.com\nggpht.com\n" +
        "openai.com\nchatgpt.com\noaistatic.com\noaiusercontent.com"
    fun domains(text: String): List<String> = text.split(Regex("[\\s,，;；]+"))
        .map { it.trim().trim('.').lowercase() }.filter { it.isNotEmpty() }.distinct().also { values ->
            require(values.size <= 500 && values.all { Profile.validDomain(it) }) { "domains" }
        }

    fun build(profile: Profile, global: Boolean, domains: List<String>): String {
        profile.validate()
        require(domains.all { Profile.validDomain(it) }) { "domains" }
        val rules = JSONArray()
            .put(JSONObject().put("action", "sniff"))
            .put(JSONObject().put("port", 53).put("action", "hijack-dns"))
            .put(JSONObject().put("ip_is_private", true).put("action", "route").put("outbound", "direct"))
            .put(JSONObject().put("inbound", JSONArray().put("health-in")).put("action", "route").put("outbound", "vless-out"))
        if (domains.isNotEmpty()) rules.put(JSONObject().put("domain_suffix", JSONArray(domains))
            .put("action", "route").put("outbound", "vless-out"))
        val tls = JSONObject().put("enabled", true).put("server_name", profile.serverName)
            .put("utls", JSONObject().put("enabled", true).put("fingerprint", "chrome"))
            .put("reality", JSONObject().put("enabled", true).put("public_key", profile.publicKey).put("short_id", profile.shortId))
        return JSONObject().put("log", JSONObject().put("level", "fatal"))
            .put("inbounds", JSONArray()
                .put(JSONObject().put("type", "mixed").put("tag", "health-in").put("listen", "127.0.0.1").put("listen_port", 2080))
                .put(JSONObject().put("type", "tun").put("tag", "tun-in")
                    .put("address", JSONArray().put("172.19.0.1/30").put("fdfe:dcba:9876::1/126"))
                    .put("mtu", 1500).put("auto_route", true).put("stack", "mixed")))
            .put("outbounds", JSONArray()
                .put(JSONObject().put("type", "vless").put("tag", "vless-out")
                    .put("server", profile.serverIp).put("server_port", profile.serverPort)
                    .put("domain_resolver", "lan-dns")
                    .put("uuid", profile.uuid).put("flow", "xtls-rprx-vision").put("tls", tls))
                .put(JSONObject().put("type", "direct").put("tag", "direct")))
            .put("dns", JSONObject().put("servers", JSONArray()
                .put(JSONObject().put("type", "local").put("tag", "lan-dns"))
                .put(JSONObject().put("type", "https").put("tag", "remote-doh")
                    .put("server", "1.1.1.1").put("server_port", 443).put("path", "/dns-query")
                    .put("tls", JSONObject().put("enabled", true).put("server_name", "cloudflare-dns.com"))
                    .put("detour", "vless-out")))
                .put("rules", JSONArray()
                    .put(JSONObject().put("domain_regex", JSONArray().put("^[^.]+$"))
                        .put("action", "route").put("server", "lan-dns"))
                    .put(JSONObject().put("domain_suffix", JSONArray(listOf("local", "lan", "home.arpa", "in-addr.arpa", "ip6.arpa")))
                        .put("action", "route").put("server", "lan-dns")))
                .put("final", "remote-doh").put("strategy", "prefer_ipv4"))
            .put("route", JSONObject().put("auto_detect_interface", true)
                .put("default_domain_resolver", JSONObject().put("server", "remote-doh").put("strategy", "prefer_ipv4"))
                .put("rules", rules).put("final", if (global) "vless-out" else "direct"))
            .toString()
    }
}
