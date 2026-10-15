// SPDX-License-Identifier: GPL-3.0-or-later
// API reference: SagerNet/sing-box v1.14.3 experimental/libbox/platform.go.
// This S2N implementation is not an official SagerNet client.
package com.cavwic.s2n.vpn

import android.net.ConnectivityManager
import android.net.NetworkCapabilities
import android.net.VpnService
import android.os.Build
import android.os.Process
import android.system.OsConstants
import io.nekohasekai.libbox.BridgeOptions
import io.nekohasekai.libbox.BridgeSession
import io.nekohasekai.libbox.ConnectionOwner
import io.nekohasekai.libbox.InterfaceUpdateListener
import io.nekohasekai.libbox.Libbox
import io.nekohasekai.libbox.LocalDNSTransport
import io.nekohasekai.libbox.NeighborUpdateListener
import io.nekohasekai.libbox.NetworkInterfaceIterator
import io.nekohasekai.libbox.Notification
import io.nekohasekai.libbox.PlatformInterface
import io.nekohasekai.libbox.PlatformUser
import io.nekohasekai.libbox.ShellSession
import io.nekohasekai.libbox.StringIterator
import io.nekohasekai.libbox.TunOptions
import io.nekohasekai.libbox.WIFIState
import java.net.InetSocketAddress
import java.net.NetworkInterface
import io.nekohasekai.libbox.NetworkInterface as BoxNetworkInterface

internal class LibboxPlatform(private val service: S2NVpnService) : PlatformInterface {
    val networkMonitor = PhysicalNetworkMonitor(service)
    private val localDns = PhysicalDnsTransport(networkMonitor)
    private val connectivity = service.getSystemService(ConnectivityManager::class.java)

    override fun usePlatformAutoDetectInterfaceControl(): Boolean = true

    override fun autoDetectInterfaceControl(fd: Int) {
        check(service.protect(fd)) { "Unable to protect uplink socket" }
    }

    override fun openTun(options: TunOptions): Int {
        check(VpnService.prepare(service) == null) { "VPN permission is missing" }
        val builder = service.newTunBuilder().setSession("S2N").setMtu(options.mtu)
        if (Build.VERSION.SDK_INT >= 29) builder.setMetered(false)
        val ipv4 = options.inet4Address
        while (ipv4.hasNext()) {
            val address = ipv4.next()
            builder.addAddress(address.address(), address.prefix())
        }
        val ipv6 = options.inet6Address
        while (ipv6.hasNext()) {
            val address = ipv6.next()
            builder.addAddress(address.address(), address.prefix())
        }
        if (options.autoRoute) {
            // libbox computes these ranges after applying all route exclusions.
            // They also work before API 33, where Builder.excludeRoute is unavailable.
            val routes4 = options.inet4RouteRange
            while (routes4.hasNext()) {
                val route = routes4.next()
                builder.addRoute(route.address(), route.prefix())
            }
            val routes6 = options.inet6RouteRange
            while (routes6.hasNext()) {
                val route = routes6.next()
                builder.addRoute(route.address(), route.prefix())
            }
            if (options.dnsMode.value != Libbox.DNSModeDisabled) {
                val servers = options.dnsServerAddress
                while (servers.hasNext()) builder.addDnsServer(servers.next())
            }
            val include = options.includePackage
            val exclude = options.excludePackage
            check(!(include.hasNext() && exclude.hasNext())) { "Conflicting application filters" }
            // Invalid package names fail closed; silently omitting every allowed app
            // would accidentally turn an allow-list into device-wide capture.
            while (include.hasNext()) builder.addAllowedApplication(include.next())
            while (exclude.hasNext()) builder.addDisallowedApplication(exclude.next())
        }
        val descriptor = builder.establish() ?: error("VPN permission was revoked")
        try {
            service.attachTun(descriptor)
        } catch (failure: Exception) {
            descriptor.close()
            throw failure
        }
        return descriptor.fd
    }

    override fun useProcFS(): Boolean = Build.VERSION.SDK_INT < 29

    @android.annotation.TargetApi(29)
    override fun findConnectionOwner(
        ipProtocol: Int,
        sourceAddress: String,
        sourcePort: Int,
        destinationAddress: String,
        destinationPort: Int,
    ): ConnectionOwner {
        check(Build.VERSION.SDK_INT >= 29) { "Connection owner API is unavailable" }
        val uid = connectivity.getConnectionOwnerUid(
            ipProtocol, InetSocketAddress(sourceAddress, sourcePort),
            InetSocketAddress(destinationAddress, destinationPort),
        )
        check(uid != Process.INVALID_UID) { "Connection owner was not found" }
        val packages = service.packageManager.getPackagesForUid(uid)?.toList().orEmpty()
        return ConnectionOwner().apply {
            userId = uid
            userName = packages.firstOrNull().orEmpty()
            setAndroidPackageNames(BoxStrings(packages))
        }
    }

    override fun startDefaultInterfaceMonitor(listener: InterfaceUpdateListener) {
        networkMonitor.setListener(listener)
    }

    override fun closeDefaultInterfaceMonitor(listener: InterfaceUpdateListener) {
        networkMonitor.setListener(null)
    }

    override fun getInterfaces(): NetworkInterfaceIterator {
        val result = mutableListOf<BoxNetworkInterface>()
        for (network in connectivity.allNetworks) {
            val capabilities = connectivity.getNetworkCapabilities(network) ?: continue
            if (!capabilities.hasCapability(NetworkCapabilities.NET_CAPABILITY_NOT_VPN)) continue
            val properties = connectivity.getLinkProperties(network) ?: continue
            val name = properties.interfaceName ?: continue
            val javaInterface = NetworkInterface.getByName(name) ?: continue
            var flags = 0
            if (javaInterface.isUp) flags = flags or OsConstants.IFF_UP or OsConstants.IFF_RUNNING
            if (javaInterface.isLoopback) flags = flags or OsConstants.IFF_LOOPBACK
            if (javaInterface.isPointToPoint) flags = flags or OsConstants.IFF_POINTOPOINT
            if (javaInterface.supportsMulticast()) flags = flags or OsConstants.IFF_MULTICAST
            result += BoxNetworkInterface().apply {
                this.name = name
                index = javaInterface.index
                mtu = javaInterface.mtu
                this.flags = flags
                addresses = BoxStrings(properties.linkAddresses.map {
                    "${it.address.hostAddress!!.substringBefore('%')}/${it.prefixLength}"
                })
                dnsServer = BoxStrings(properties.dnsServers.mapNotNull { it.hostAddress?.substringBefore('%') })
                gateway = BoxStrings(properties.routes.filter { it.isDefaultRoute }
                    .mapNotNull { it.gateway?.takeUnless { address -> address.isAnyLocalAddress }?.hostAddress?.substringBefore('%') })
                metered = !capabilities.hasCapability(NetworkCapabilities.NET_CAPABILITY_NOT_METERED)
                type = when {
                    capabilities.hasTransport(NetworkCapabilities.TRANSPORT_WIFI) -> Libbox.InterfaceTypeWIFI
                    capabilities.hasTransport(NetworkCapabilities.TRANSPORT_CELLULAR) -> Libbox.InterfaceTypeCellular
                    capabilities.hasTransport(NetworkCapabilities.TRANSPORT_ETHERNET) -> Libbox.InterfaceTypeEthernet
                    else -> Libbox.InterfaceTypeOther
                }
            }
        }
        return BoxInterfaces(result.iterator())
    }

    override fun localDNSTransport(): LocalDNSTransport = localDns

    fun close() {
        try {
            localDns.close()
        } finally {
            networkMonitor.close()
        }
    }
    override fun underNetworkExtension(): Boolean = false
    override fun includeAllNetworks(): Boolean = false
    override fun readWIFIState(): WIFIState? = null
    override fun clearDNSCache() = Unit
    override fun sendNotification(notification: Notification?) = Unit
    override fun cancelNotification(identifier: String?, typeID: Int) = Unit
    override fun startNeighborMonitor(listener: NeighborUpdateListener?) = Unit
    override fun closeNeighborMonitor(listener: NeighborUpdateListener?) = Unit
    override fun registerMyInterface(name: String?) = Unit
    override fun usePlatformShell(): Boolean = false
    override fun checkPlatformShell() = throw UnsupportedOperationException("Shell is unavailable")
    override fun openShellSession(
        user: PlatformUser?, command: String?, environ: StringIterator?, term: String?, rows: Int, cols: Int,
    ): ShellSession = throw UnsupportedOperationException("Shell is unavailable")
    override fun lookupUser(username: String?): PlatformUser = throw UnsupportedOperationException("User lookup is unavailable")
    override fun lookupSFTPServer(): String = throw UnsupportedOperationException("SFTP is unavailable")
    override fun readSystemSSHHostKey(): String = throw UnsupportedOperationException("SSH host keys are unavailable")
    override fun tailscaleHostname(): String = "S2N"
    override fun usePlatformBridge(): Boolean = false
    override fun createBridge(options: BridgeOptions?): BridgeSession = throw UnsupportedOperationException("Bridging is unavailable")
}

private class BoxStrings(private val values: List<String>) : StringIterator {
    private var offset = 0
    override fun len(): Int = values.size - offset
    override fun hasNext(): Boolean = offset < values.size
    override fun next(): String = values[offset++]
}

private class BoxInterfaces(private val values: Iterator<BoxNetworkInterface>) : NetworkInterfaceIterator {
    override fun hasNext(): Boolean = values.hasNext()
    override fun next(): BoxNetworkInterface = values.next()
}
