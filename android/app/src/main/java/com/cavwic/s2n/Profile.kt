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
    val publicKey: String, val shortId: String, val serverName: String,
    val transport: String = "vless", val password: String = "", val certificatePem: String = ""
) {
    fun validate(): Profile {
        require(transport in listOf("vless", "hysteria2")) { "protocol" }
        require(validHost(serverIp)) { "server" }
        require(serverPort in 1..65535) { "port" }
        require(validHost(serverName)) { "SNI" }
        if (transport == "hysteria2") {
            require(password.isNotBlank() && password.length <= 4096 && password.none { it.isISOControl() }) { "password" }
            require(validCertificates(certificatePem)) { "certificate" }
        } else {
            require(runCatching { UUID.fromString(uuid).toString().equals(uuid, true) && UUID.fromString(uuid) != UUID(0L, 0L) }.getOrDefault(false)) { "uuid" }
            require(Regex("[A-Za-z0-9_-]{43}").matches(publicKey) &&
                runCatching { Base64.getUrlDecoder().decode(publicKey).size == 32 }.getOrDefault(false)) { "public key" }
            require(shortId.length <= 16 && shortId.length % 2 == 0 && Regex("[0-9a-fA-F]*").matches(shortId)) { "short id" }
        }
        return this
    }

    fun toJson(): JSONObject {
        val json = JSONObject().put("serverIp", serverIp).put("serverPort", serverPort).put("serverName", serverName)
        return if (transport == "hysteria2") json.put("schemaVersion", 2).put("transport", transport)
            .put("password", password).put("certificatePem", certificatePem)
        else json.put("uuid", uuid).put("publicKey", publicKey).put("shortId", shortId)
    }

    companion object {
        fun parse(text: String): Profile {
            require(text.length <= 64 * 1024) { "size" }
            val input = text.trim()
            if (input.startsWith("vless://", true) || input.startsWith("hysteria2://", true) || input.startsWith("hy2://", true))
                return fromUri(input).validate()
            val json = JSONObject(input)
            val allKeys = json.keys().asSequence().toList()
            require(allKeys.map { it.lowercase() }.distinct().size == allKeys.size) { "duplicate fields" }
            val keys = allKeys.associateBy { it.lowercase() }
            fun str(key: String, trim: Boolean = true): String {
                val value = json.opt(keys[key.lowercase()] ?: key)
                return if (value is String) if (trim) value.trim() else value else ""
            }
            val schemaKey = keys["schemaversion"]
            val schema = if (schemaKey == null) 1 else {
                val value = json.opt(schemaKey)
                require(value is Number && value.toDouble() == value.toInt().toDouble()) { "schema" }
                value.toInt()
            }
            require(schema in 1..2) { "schema" }
            val protocol = if (keys.containsKey("transport")) str("transport").lowercase() else "vless"
            require(protocol in listOf("vless", "hysteria2")) { "protocol" }
            if (protocol == "hysteria2") require(schema == 2) { "schema" }
            val portValue = json.opt(keys["serverport"] ?: "serverPort")
            val port = if (portValue is Number) {
                require(portValue.toDouble() == portValue.toInt().toDouble()) { "port" }
                portValue.toInt()
            } else str("serverPort").toIntOrNull() ?: 0
            return Profile(str("serverIp"), port,
                str("uuid"), str("publicKey"), str("shortId"), str("serverName"), protocol,
                str("password", false), str("certificatePem").replace("\r\n", "\n").let { if (it.isEmpty()) it else it + "\n" }).validate()
        }

        private fun fromUri(text: String): Profile {
            val uri = URI(text)
            fun decode(value: String) = URLDecoder.decode(value.replace("+", "%2B"), "UTF-8")
            val pairs = uri.rawQuery.orEmpty().split('&').filter { it.isNotEmpty() }.map {
                val parts = it.split('=', limit = 2)
                decode(parts[0]) to decode(parts.getOrElse(1) { "" })
            }
            require(pairs.map { it.first }.distinct().size == pairs.size) { "duplicate parameters" }
            val args = pairs.toMap()
            require(uri.rawUserInfo != null) { "credentials" }
            require(uri.port == -1 || uri.port in 1..65535) { "port" }
            val host = uri.host.orEmpty().removeSurrounding("[", "]")
            if (uri.scheme.equals("hysteria2", true) || uri.scheme.equals("hy2", true)) {
                require(uri.path.orEmpty() in listOf("", "/")) { "path" }
                require(args.keys.all { it in listOf("sni", "insecure") }) { "unsupported parameters" }
                require(args["insecure"].orEmpty() in listOf("", "0", "false")) { "TLS verification" }
                return Profile(host, if (uri.port == -1) 443 else uri.port, "", "", "",
                    args["sni"]?.takeIf { it.isNotBlank() } ?: host, "hysteria2", decode(uri.rawUserInfo))
            }
            require(args["security"] == "reality") { "security" }
            require(args["type"].orEmpty() in listOf("", "tcp", "raw")) { "transport" }
            require(args["flow"].orEmpty() in listOf("", "xtls-rprx-vision")) { "flow" }
            require(!uri.rawUserInfo.contains(':')) { "uuid" }
            return Profile(host, if (uri.port == -1) 443 else uri.port,
                decode(uri.rawUserInfo), args["pbk"].orEmpty(), args["sid"].orEmpty(), args["sni"].orEmpty())
        }

        private fun validCertificates(value: String): Boolean {
            if (value.isBlank()) return true
            if (value.length > 32768 || value.contains("PRIVATE KEY", true)) return false
            val pattern = Regex("-----BEGIN CERTIFICATE-----\\s+[A-Za-z0-9+/=\\s]+?-----END CERTIFICATE-----")
            val matches = pattern.findAll(value).toList()
            if (matches.size !in 1..8 || value.replace(pattern, "").isNotBlank()) return false
            return runCatching {
                matches.forEach { match ->
                    java.security.cert.CertificateFactory.getInstance("X.509")
                        .generateCertificate(match.value.byteInputStream(Charsets.US_ASCII))
                }
                true
            }.getOrDefault(false)
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
        val outbound = JSONObject().put("type", profile.transport).put("tag", "vless-out")
            .put("server", profile.serverIp).put("server_port", profile.serverPort).put("domain_resolver", "lan-dns")
        if (profile.transport == "hysteria2") {
            tls.put("insecure", false)
            if (profile.certificatePem.isNotBlank())
                tls.put("certificate", JSONArray(profile.certificatePem.lines().filter { it.isNotEmpty() }))
            outbound.put("password", profile.password)
        } else {
            tls.put("utls", JSONObject().put("enabled", true).put("fingerprint", "chrome"))
                .put("reality", JSONObject().put("enabled", true).put("public_key", profile.publicKey).put("short_id", profile.shortId))
            outbound.put("uuid", profile.uuid).put("flow", "xtls-rprx-vision")
        }
        outbound.put("tls", tls)
        return JSONObject().put("log", JSONObject().put("level", "fatal"))
            .put("inbounds", JSONArray()
                .put(JSONObject().put("type", "mixed").put("tag", "health-in").put("listen", "127.0.0.1").put("listen_port", 2080))
                .put(JSONObject().put("type", "tun").put("tag", "tun-in")
                    .put("address", JSONArray().put("172.19.0.1/30").put("fdfe:dcba:9876::1/126"))
                    .put("mtu", 1500).put("auto_route", true).put("stack", "mixed")))
            .put("outbounds", JSONArray()
                .put(outbound)
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
