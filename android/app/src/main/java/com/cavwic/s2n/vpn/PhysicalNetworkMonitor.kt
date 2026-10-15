// SPDX-License-Identifier: GPL-3.0-or-later
package com.cavwic.s2n.vpn

import android.net.ConnectivityManager
import android.net.LinkProperties
import android.net.Network
import android.net.NetworkCapabilities
import android.net.NetworkRequest
import android.os.Build
import android.os.Handler
import android.os.HandlerThread
import io.nekohasekai.libbox.InterfaceUpdateListener
import java.net.NetworkInterface

/** Tracks the physical uplink, never the VPN's own default route. */
internal class PhysicalNetworkMonitor(private val service: S2NVpnService) {
    private val connectivity = service.getSystemService(ConnectivityManager::class.java)
    private val thread = HandlerThread("s2n-network")
    private var registered = false
    @Volatile private var closed = false
    @Volatile private var listener: InterfaceUpdateListener? = null
    @Volatile var currentNetwork: Network? = null
        private set

    private val callback = object : ConnectivityManager.NetworkCallback() {
        override fun onAvailable(network: Network) {
            if (closed) return
            currentNetwork = network
            publish(network)
        }

        override fun onLinkPropertiesChanged(network: Network, properties: LinkProperties) {
            if (!closed && network == currentNetwork) publish(network, properties)
        }

        override fun onCapabilitiesChanged(network: Network, capabilities: NetworkCapabilities) {
            if (!closed && network == currentNetwork) publish(network)
        }

        override fun onLost(network: Network) {
            if (closed || network != currentNetwork) return
            currentNetwork = null
            listener?.updateDefaultInterface("", -1, false, false)
            service.updateUnderlyingNetwork()
        }
    }

    @Synchronized
    fun start() {
        if (registered) return
        check(!closed)
        thread.start()
        val handler = Handler(thread.looper)
        val request = NetworkRequest.Builder()
            .addCapability(NetworkCapabilities.NET_CAPABILITY_INTERNET)
            .addCapability(NetworkCapabilities.NET_CAPABILITY_NOT_VPN)
            .build()
        currentNetwork = connectivity.activeNetwork?.takeIf { isPhysical(it) }
            ?: connectivity.allNetworks.firstOrNull { isPhysical(it) }
        try {
            when {
                Build.VERSION.SDK_INT >= 31 -> connectivity.registerBestMatchingNetworkCallback(request, callback, handler)
                Build.VERSION.SDK_INT >= 28 -> connectivity.requestNetwork(request, callback, handler)
                else -> connectivity.registerDefaultNetworkCallback(callback, handler)
            }
            registered = true
            currentNetwork?.let { publish(it) }
        } catch (failure: Exception) {
            closed = true
            thread.quitSafely()
            throw failure
        }
    }

    fun setListener(value: InterfaceUpdateListener?) {
        listener = value
        currentNetwork?.let { publish(it) }
            ?: value?.updateDefaultInterface("", -1, false, false)
    }

    private fun isPhysical(network: Network): Boolean =
        connectivity.getNetworkCapabilities(network)?.let {
            it.hasCapability(NetworkCapabilities.NET_CAPABILITY_INTERNET) &&
                it.hasCapability(NetworkCapabilities.NET_CAPABILITY_NOT_VPN)
        } == true

    private fun publish(network: Network, suppliedProperties: LinkProperties? = null) {
        if (closed || !isPhysical(network)) return
        val properties = suppliedProperties ?: connectivity.getLinkProperties(network) ?: return
        val name = properties.interfaceName ?: return
        val index = runCatching { NetworkInterface.getByName(name)?.index }.getOrNull() ?: return
        val capabilities = connectivity.getNetworkCapabilities(network) ?: return
        val metered = !capabilities.hasCapability(NetworkCapabilities.NET_CAPABILITY_NOT_METERED)
        listener?.updateDefaultInterface(name, index, metered, false)
        service.updateUnderlyingNetwork()
    }

    @Synchronized
    fun close() {
        if (closed) return
        closed = true
        listener = null
        currentNetwork = null
        if (registered) {
            registered = false
            try {
                connectivity.unregisterNetworkCallback(callback)
            } finally {
                thread.quitSafely()
            }
        } else {
            thread.quitSafely()
        }
    }
}
