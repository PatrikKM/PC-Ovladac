package cz.pcovladac.app.data

import android.content.Context
import android.net.nsd.NsdManager
import android.net.nsd.NsdServiceInfo
import android.net.wifi.WifiManager
import android.os.Build
import java.net.Inet4Address
import java.util.concurrent.Executors

class NsdDiscoveryManager(
    context: Context,
    private val onServiceFound: (host: String, port: Int, serviceName: String) -> Unit,
    private val onStatusChange: (String) -> Unit
) {
    private val nsdManager = context.applicationContext.getSystemService(Context.NSD_SERVICE) as NsdManager
    private val wifiManager = context.applicationContext.getSystemService(Context.WIFI_SERVICE) as? WifiManager
    private val resolveExecutor = Executors.newSingleThreadExecutor()

    private var multicastLock: WifiManager.MulticastLock? = null
    private val activeListeners = mutableListOf<NsdManager.DiscoveryListener>()
    private var isDiscovering = false

    fun startDiscovery() {
        stopDiscovery()
        isDiscovering = true
        onStatusChange("Hledám službu PC-Ovladac v domácí Wi-Fi...")

        try {
            multicastLock = wifiManager?.createMulticastLock("pc-ovladac-nsd")?.apply {
                setReferenceCounted(true)
                acquire()
            }
        } catch (_: Exception) {
        }

        // Hledáme vlastní typ i obecný _http._tcp.
        discoverServiceType("_pcovladac._tcp.")
        discoverServiceType("_http._tcp.")
    }

    private fun discoverServiceType(serviceType: String) {
        val listener = object : NsdManager.DiscoveryListener {
            override fun onDiscoveryStarted(regType: String) {}

            override fun onServiceFound(service: NsdServiceInfo) {
                if (!service.serviceName.contains("PC-Ovladac", ignoreCase = true)) {
                    return
                }
                resolveFoundService(service)
            }

            override fun onServiceLost(service: NsdServiceInfo) {}

            override fun onDiscoveryStopped(serviceType: String) {}

            override fun onStartDiscoveryFailed(serviceType: String, errorCode: Int) {
                try { nsdManager.stopServiceDiscovery(this) } catch (_: Exception) {}
            }

            override fun onStopDiscoveryFailed(serviceType: String, errorCode: Int) {
                try { nsdManager.stopServiceDiscovery(this) } catch (_: Exception) {}
            }
        }

        try {
            nsdManager.discoverServices(serviceType, NsdManager.PROTOCOL_DNS_SD, listener)
            activeListeners.add(listener)
        } catch (_: Exception) {
        }
    }

    @Suppress("DEPRECATION")
    private fun resolveFoundService(service: NsdServiceInfo) {
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.UPSIDE_DOWN_CAKE) {
            try {
                nsdManager.registerServiceInfoCallback(
                    service,
                    resolveExecutor,
                    object : NsdManager.ServiceInfoCallback {
                        override fun onServiceInfoCallbackRegistrationFailed(errorCode: Int) {}

                        override fun onServiceUpdated(serviceInfo: NsdServiceInfo) {
                            val ipv4 = serviceInfo.hostAddresses.firstOrNull { it is Inet4Address }?.hostAddress
                                ?: serviceInfo.hostAddresses.firstOrNull()?.hostAddress
                            if (!ipv4.isNullOrBlank() && serviceInfo.port > 0) {
                                onServiceFound(ipv4, serviceInfo.port, serviceInfo.serviceName)
                                try { nsdManager.unregisterServiceInfoCallback(this) } catch (_: Exception) {}
                            }
                        }

                        override fun onServiceLost() {}
                        override fun onServiceInfoCallbackUnregistered() {}
                    }
                )
            } catch (_: Exception) {
            }
        } else {
            try {
                nsdManager.resolveService(service, object : NsdManager.ResolveListener {
                    override fun onResolveFailed(serviceInfo: NsdServiceInfo, errorCode: Int) {}
                    override fun onServiceResolved(serviceInfo: NsdServiceInfo) {
                        val host = serviceInfo.host?.hostAddress
                        if (!host.isNullOrBlank() && serviceInfo.port > 0) {
                            onServiceFound(host, serviceInfo.port, serviceInfo.serviceName)
                        }
                    }
                })
            } catch (_: Exception) {
            }
        }
    }

    fun stopDiscovery() {
        isDiscovering = false
        for (listener in activeListeners) {
            try {
                nsdManager.stopServiceDiscovery(listener)
            } catch (_: Exception) {
            }
        }
        activeListeners.clear()

        try {
            multicastLock?.let { if (it.isHeld) it.release() }
        } catch (_: Exception) {
        }
        multicastLock = null
    }
}
