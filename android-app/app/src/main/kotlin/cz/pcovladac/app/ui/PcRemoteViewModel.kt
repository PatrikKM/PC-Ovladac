package cz.pcovladac.app.ui

import android.app.Application
import android.content.Context
import androidx.lifecycle.AndroidViewModel
import androidx.lifecycle.viewModelScope
import cz.pcovladac.app.data.NsdDiscoveryManager
import cz.pcovladac.app.data.PcApiClient
import cz.pcovladac.app.data.PcCommand
import cz.pcovladac.app.data.VoiceCommandMatcher
import kotlinx.coroutines.flow.MutableSharedFlow
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.SharedFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asSharedFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.update
import kotlinx.coroutines.launch

data class PcRemoteUiState(
    val hostInput: String = "",
    val portInput: String = "5000",
    val connectedHost: String? = null,
    val connectedPort: Int = 5000,
    val isConnected: Boolean = false,
    val isSearchingNsd: Boolean = false,
    val statusText: String = "Hledám PC-Ovladac v síti...",
    val commands: List<PcCommand> = PcApiClient.defaultCommands,
    val isSending: Boolean = false,
    val lastSpokenText: String? = null
)

class PcRemoteViewModel(application: Application) : AndroidViewModel(application) {

    private val prefs = application.getSharedPreferences("pc_ovladac_prefs", Context.MODE_PRIVATE)

    private val _uiState = MutableStateFlow(
        PcRemoteUiState(
            hostInput = prefs.getString("host", "") ?: "",
            portInput = prefs.getInt("port", 5000).toString()
        )
    )
    val uiState: StateFlow<PcRemoteUiState> = _uiState.asStateFlow()

    private val _messages = MutableSharedFlow<String>(extraBufferCapacity = 4)
    val messages: SharedFlow<String> = _messages.asSharedFlow()

    private val nsdDiscovery = NsdDiscoveryManager(
        context = application,
        onServiceFound = { host, port, serviceName ->
            viewModelScope.launch {
                _uiState.update {
                    it.copy(
                        hostInput = host,
                        portInput = port.toString(),
                        isSearchingNsd = false,
                        statusText = "Nalezeno přes mDNS: $serviceName ($host:$port)"
                    )
                }
                connectToPc(host, port, silentIfFailed = false)
            }
        },
        onStatusChange = { status ->
            _uiState.update { it.copy(statusText = status) }
        }
    )

    init {
        startNsdDiscovery()
        val savedHost = _uiState.value.hostInput.trim()
        val savedPort = _uiState.value.portInput.toIntOrNull() ?: 5000
        if (savedHost.isNotBlank()) {
            connectToPc(savedHost, savedPort, silentIfFailed = true)
        }
    }

    fun onHostChanged(value: String) {
        _uiState.update { it.copy(hostInput = value) }
    }

    fun onPortChanged(value: String) {
        val digits = value.filter { it.isDigit() }.take(5)
        _uiState.update { it.copy(portInput = digits) }
    }

    fun startNsdDiscovery() {
        _uiState.update { it.copy(isSearchingNsd = true) }
        nsdDiscovery.startDiscovery()
    }

    fun connectManual() {
        val host = _uiState.value.hostInput.trim()
        val port = _uiState.value.portInput.toIntOrNull() ?: 5000
        if (host.isBlank()) {
            _messages.tryEmit("Zadejte IP adresu PC nebo počkejte na vyhledání přes mDNS.")
            return
        }
        connectToPc(host, port, silentIfFailed = false)
    }

    private fun connectToPc(host: String, port: Int, silentIfFailed: Boolean) {
        viewModelScope.launch {
            _uiState.update { it.copy(statusText = "Připojuji se k $host:$port...") }
            val result = PcApiClient.fetchCommands(host, port)
            result.fold(
                onSuccess = { cmds ->
                    prefs.edit().putString("host", host).putInt("port", port).apply()
                    _uiState.update {
                        it.copy(
                            connectedHost = host,
                            connectedPort = port,
                            isConnected = true,
                            isSearchingNsd = false,
                            commands = cmds,
                            statusText = "Připojeno k PC ($host:$port) — načteno ${cmds.size} příkazů"
                        )
                    }
                    if (!silentIfFailed) {
                        _messages.tryEmit("Připojeno k PC ($host:$port)")
                    }
                },
                onFailure = { err ->
                    _uiState.update {
                        it.copy(
                            isConnected = false,
                            statusText = "Nepodařilo se připojit k $host:$port"
                        )
                    }
                    if (!silentIfFailed) {
                        _messages.tryEmit("Chyba připojení: ${err.localizedMessage ?: "PC neodpovídá"}")
                    }
                }
            )
        }
    }

    fun sendCommand(commandName: String) {
        val state = _uiState.value
        val host = state.connectedHost ?: state.hostInput.trim()
        val port = if (state.connectedHost != null) state.connectedPort else (state.portInput.toIntOrNull() ?: 5000)

        if (host.isBlank()) {
            _messages.tryEmit("Nejprve vyhledejte PC nebo zadejte IP adresu.")
            return
        }

        viewModelScope.launch {
            _uiState.update { it.copy(isSending = true) }
            val res = PcApiClient.sendCommand(host, port, commandName)
            _uiState.update { it.copy(isSending = false) }
            res.fold(
                onSuccess = { msg ->
                    prefs.edit().putString("host", host).putInt("port", port).apply()
                    _uiState.update {
                        it.copy(
                            connectedHost = host,
                            connectedPort = port,
                            isConnected = true
                        )
                    }
                    _messages.tryEmit("✓ $msg")
                },
                onFailure = { err ->
                    _messages.tryEmit("✗ ${err.localizedMessage ?: "Příkaz se nepodařilo odeslat"}")
                }
            )
        }
    }

    fun handleVoiceResult(spokenText: String) {
        val matched = VoiceCommandMatcher.matchCommand(spokenText, _uiState.value.commands)
        _uiState.update { it.copy(lastSpokenText = "„$spokenText“ → $matched") }
        if (matched.isNotBlank()) {
            sendCommand(matched)
        } else {
            _messages.tryEmit("Hlasový povel nebyl rozpoznán.")
        }
    }

    override fun onCleared() {
        super.onCleared()
        nsdDiscovery.stopDiscovery()
    }
}
