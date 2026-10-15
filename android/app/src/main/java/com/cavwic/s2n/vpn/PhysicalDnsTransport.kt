// SPDX-License-Identifier: GPL-3.0-or-later
package com.cavwic.s2n.vpn

import android.net.DnsResolver
import android.os.Build
import android.os.CancellationSignal
import io.nekohasekai.libbox.ExchangeContext
import io.nekohasekai.libbox.LocalDNSTransport
import java.net.Inet4Address
import java.net.Inet6Address
import java.net.SocketTimeoutException
import java.net.UnknownHostException
import java.util.concurrent.CancellationException
import java.util.concurrent.CountDownLatch
import java.util.concurrent.Executors
import java.util.concurrent.TimeUnit
import java.util.concurrent.atomic.AtomicBoolean
import java.util.concurrent.atomic.AtomicReference

/** Resolves on the physical network, outside the VPN and its remote-DNS path. */
internal class PhysicalDnsTransport(private val monitor: PhysicalNetworkMonitor) : LocalDNSTransport {
    private val callbacks = Executors.newSingleThreadExecutor()

    override fun raw(): Boolean = Build.VERSION.SDK_INT >= 29

    override fun lookup(ctx: ExchangeContext, network: String, domain: String) {
        val uplink = monitor.currentNetwork ?: error("No physical network")
        val cancelled = AtomicBoolean()
        ctx.onCancel { cancelled.set(true) }
        try {
            val addresses = uplink.getAllByName(domain).filter {
                when {
                    network.endsWith("4") -> it is Inet4Address
                    network.endsWith("6") -> it is Inet6Address
                    else -> true
                }
            }
            if (cancelled.get()) throw CancellationException()
            ctx.success(addresses.mapNotNull { it.hostAddress }.joinToString("\n"))
        } catch (_: UnknownHostException) {
            ctx.errorCode(3) // DNS NXDOMAIN, without exposing the requested name.
        }
    }

    @android.annotation.TargetApi(29)
    override fun exchange(ctx: ExchangeContext, message: ByteArray) {
        check(Build.VERSION.SDK_INT >= 29)
        val uplink = monitor.currentNetwork ?: error("No physical network")
        val signal = CancellationSignal()
        val completed = AtomicBoolean()
        val finished = CountDownLatch(1)
        val failure = AtomicReference<Exception?>()

        fun finish(operation: () -> Unit) {
            if (!completed.compareAndSet(false, true)) return
            try {
                operation()
            } catch (exception: Exception) {
                failure.set(exception)
            } finally {
                finished.countDown()
            }
        }
        ctx.onCancel {
            finish {
                signal.cancel()
                throw CancellationException()
            }
        }
        DnsResolver.getInstance().rawQuery(
            uplink, message, DnsResolver.FLAG_NO_RETRY, callbacks, signal,
            object : DnsResolver.Callback<ByteArray> {
                override fun onAnswer(answer: ByteArray, rcode: Int) = finish {
                    if (rcode == 0) ctx.rawSuccess(answer) else ctx.errorCode(rcode)
                }

                override fun onError(error: DnsResolver.DnsException) = finish {
                    throw error
                }
            },
        )
        if (!finished.await(15, TimeUnit.SECONDS)) {
            finish { signal.cancel() }
            throw SocketTimeoutException("Physical DNS timed out")
        }
        failure.get()?.let { throw it }
    }

    fun close() {
        callbacks.shutdownNow()
    }
}
