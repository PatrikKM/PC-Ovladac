package cz.pcovladac.app

import android.app.Activity
import android.content.Intent
import android.graphics.BitmapFactory
import android.os.Bundle
import android.speech.RecognizerIntent
import android.util.Base64
import androidx.activity.ComponentActivity
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.compose.setContent
import androidx.activity.enableEdgeToEdge
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.Image
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.lazy.grid.GridCells
import androidx.compose.foundation.lazy.grid.LazyVerticalGrid
import androidx.compose.foundation.lazy.grid.items
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.material3.Button
import androidx.compose.material3.Card
import androidx.compose.material3.CardDefaults
import androidx.compose.material3.ElevatedButton
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.ExtendedFloatingActionButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Scaffold
import androidx.compose.material3.SnackbarHost
import androidx.compose.material3.SnackbarHostState
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.material3.TopAppBar
import androidx.compose.material3.TopAppBarDefaults
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.asImageBitmap
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import androidx.lifecycle.viewmodel.compose.viewModel
import cz.pcovladac.app.data.PcCommand
import cz.pcovladac.app.ui.PcRemoteViewModel
import cz.pcovladac.app.ui.theme.PCOvladacTheme

class MainActivity : ComponentActivity() {
    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        enableEdgeToEdge()
        setContent {
            PCOvladacTheme {
                PcRemoteScreen()
            }
        }
    }
}

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun PcRemoteScreen(vm: PcRemoteViewModel = viewModel()) {
    val state by vm.uiState.collectAsStateWithLifecycle()
    val snackbarHostState = remember { SnackbarHostState() }
    var showManualSettings by remember { mutableStateOf(false) }

    LaunchedEffect(Unit) {
        vm.messages.collect { msg ->
            snackbarHostState.showSnackbar(msg)
        }
    }

    val voiceLauncher = rememberLauncherForActivityResult(
        contract = ActivityResultContracts.StartActivityForResult()
    ) { result ->
        if (result.resultCode == Activity.RESULT_OK) {
            val matches = result.data?.getStringArrayListExtra(RecognizerIntent.EXTRA_RESULTS)
            val spoken = matches?.firstOrNull()
            if (!spoken.isNullOrBlank()) {
                vm.handleVoiceResult(spoken)
            }
        }
    }

    fun launchCzechVoiceInput() {
        val intent = Intent(RecognizerIntent.ACTION_RECOGNIZE_SPEECH).apply {
            putExtra(RecognizerIntent.EXTRA_LANGUAGE_MODEL, RecognizerIntent.LANGUAGE_MODEL_FREE_FORM)
            putExtra(RecognizerIntent.EXTRA_LANGUAGE, "cs-CZ")
            putExtra(RecognizerIntent.EXTRA_PROMPT, "Řekněte název příkazu (např. ztlumit, pauza, další)...")
        }
        try {
            voiceLauncher.launch(intent)
        } catch (_: Exception) {
            // Pokud zařízení nemá nainstalované rozpoznávání řeči
        }
    }

    Scaffold(
        topBar = {
            TopAppBar(
                title = {
                    Text(
                        text = "PC Ovladač",
                        fontWeight = FontWeight.Bold
                    )
                },
                actions = {
                    TextButton(onClick = { vm.connectManual() }) {
                        Text("Obnovit")
                    }
                },
                colors = TopAppBarDefaults.topAppBarColors(
                    containerColor = MaterialTheme.colorScheme.primaryContainer,
                    titleContentColor = MaterialTheme.colorScheme.onPrimaryContainer
                )
            )
        },
        snackbarHost = { SnackbarHost(snackbarHostState) },
        floatingActionButton = {
            ExtendedFloatingActionButton(
                onClick = { launchCzechVoiceInput() },
                containerColor = MaterialTheme.colorScheme.primary,
                contentColor = MaterialTheme.colorScheme.onPrimary
            ) {
                Text(text = "🎤", fontSize = 18.sp)
                Spacer(modifier = Modifier.width(8.dp))
                Text(text = "Hlasový povel", fontWeight = FontWeight.SemiBold)
            }
        }
    ) { innerPadding ->
        Column(
            modifier = Modifier
                .fillMaxSize()
                .padding(innerPadding)
                .padding(horizontal = 14.dp, vertical = 8.dp)
        ) {
            // Kompaktní pruh stavu připojení + NSD / ruční IP
            Card(
                modifier = Modifier.fillMaxWidth(),
                colors = CardDefaults.cardColors(
                    containerColor = MaterialTheme.colorScheme.surfaceVariant
                ),
                shape = RoundedCornerShape(12.dp)
            ) {
                Column(modifier = Modifier.padding(horizontal = 12.dp, vertical = 6.dp)) {
                    Row(
                        verticalAlignment = Alignment.CenterVertically,
                        modifier = Modifier.fillMaxWidth()
                    ) {
                        Box(
                            modifier = Modifier
                                .size(8.dp)
                                .clip(CircleShape)
                                .background(
                                    if (state.isConnected) Color(0xFF16A34A)
                                    else Color(0xFFD97706)
                                )
                        )
                        Spacer(modifier = Modifier.width(8.dp))
                        Text(
                            text = if (state.isConnected && state.connectedHost != null) {
                                "PC ${state.connectedHost}:${state.connectedPort}"
                            } else {
                                state.statusText
                            },
                            style = MaterialTheme.typography.bodySmall,
                            fontWeight = FontWeight.SemiBold,
                            maxLines = 1,
                            overflow = TextOverflow.Ellipsis,
                            modifier = Modifier.weight(1f)
                        )
                        TextButton(
                            onClick = { vm.startNsdDiscovery() },
                            contentPadding = PaddingValues(horizontal = 8.dp, vertical = 2.dp)
                        ) {
                            Text(
                                text = if (state.isSearchingNsd) "…" else "mDNS",
                                fontSize = 12.sp
                            )
                        }
                        TextButton(
                            onClick = { showManualSettings = !showManualSettings },
                            contentPadding = PaddingValues(horizontal = 8.dp, vertical = 2.dp)
                        ) {
                            Text(
                                text = if (showManualSettings) "Skrýt" else "IP",
                                fontSize = 12.sp
                            )
                        }
                    }

                    if (showManualSettings || !state.isConnected) {
                        Spacer(modifier = Modifier.height(6.dp))
                        Row(
                            horizontalArrangement = Arrangement.spacedBy(8.dp),
                            verticalAlignment = Alignment.CenterVertically
                        ) {
                            OutlinedTextField(
                                value = state.hostInput,
                                onValueChange = vm::onHostChanged,
                                label = { Text("IP adresa PC") },
                                placeholder = { Text("192.168.0.105") },
                                singleLine = true,
                                modifier = Modifier.weight(2f)
                            )
                            OutlinedTextField(
                                value = state.portInput,
                                onValueChange = vm::onPortChanged,
                                label = { Text("Port") },
                                singleLine = true,
                                keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Number),
                                modifier = Modifier.weight(1f)
                            )
                        }
                        Spacer(modifier = Modifier.height(6.dp))
                        Button(
                            onClick = { vm.connectManual() },
                            modifier = Modifier.fillMaxWidth()
                        ) {
                            Text("Připojit")
                        }
                    }
                }
            }

            if (!state.lastSpokenText.isNullOrBlank()) {
                Spacer(modifier = Modifier.height(4.dp))
                Text(
                    text = "Hlas: ${state.lastSpokenText}",
                    style = MaterialTheme.typography.bodySmall,
                    color = MaterialTheme.colorScheme.onSurfaceVariant
                )
            }

            Spacer(modifier = Modifier.height(8.dp))

            LazyVerticalGrid(
                columns = GridCells.Fixed(2),
                horizontalArrangement = Arrangement.spacedBy(10.dp),
                verticalArrangement = Arrangement.spacedBy(10.dp),
                contentPadding = PaddingValues(bottom = 84.dp),
                modifier = Modifier.fillMaxSize()
            ) {
                items(state.commands, key = { it.name }) { cmd ->
                    CommandActionButton(
                        command = cmd,
                        enabled = !state.isSending,
                        onClick = { vm.sendCommand(cmd.name) }
                    )
                }
            }
        }
    }
}

@Composable
private fun CommandActionButton(
    command: PcCommand,
    enabled: Boolean,
    onClick: () -> Unit
) {
    val iconBitmap = remember(command.iconBase64) {
        if (command.iconBase64.isNotBlank()) {
            try {
                val bytes = Base64.decode(command.iconBase64, Base64.DEFAULT)
                BitmapFactory.decodeByteArray(bytes, 0, bytes.size)?.asImageBitmap()
            } catch (_: Exception) {
                null
            }
        } else {
            null
        }
    }

    val fallbackEmoji = when {
        command.name.equals("f11", ignoreCase = true) || command.name.equals("obrazovka", ignoreCase = true) -> "⛶"
        command.name.equals("alt_tab", ignoreCase = true) || command.name.equals("tab", ignoreCase = true) -> "🔀"
        command.actionType == "VolumeUp" -> "🔊"
        command.actionType == "VolumeDown" -> "🔉"
        command.actionType == "VolumeMute" -> "🔇"
        command.actionType == "MediaPlayPause" -> "⏯️"
        command.actionType == "MediaNext" -> "⏭️"
        command.actionType == "MediaPrevious" -> "⏮️"
        command.actionType == "LockScreen" -> "🔒"
        command.actionType == "Sleep" -> "🌙"
        command.actionType == "Shutdown" -> "⏻"
        command.actionType == "RunProgram" -> "🚀"
        command.actionType == "Shortcut" -> "⌨️"
        else -> "⚡"
    }

    val buttonLabel = when {
        command.name.equals("f11", ignoreCase = true) -> "Celá obrazovka (F11)"
        command.name.equals("alt_tab", ignoreCase = true) -> "Přepnout okno (Alt+Tab)"
        else -> command.displayName.ifBlank { command.name }
    }

    ElevatedButton(
        onClick = onClick,
        enabled = enabled,
        shape = RoundedCornerShape(14.dp),
        contentPadding = PaddingValues(horizontal = 10.dp, vertical = 12.dp),
        modifier = Modifier
            .fillMaxWidth()
            .height(92.dp)
    ) {
        Column(
            horizontalAlignment = Alignment.CenterHorizontally,
            verticalArrangement = Arrangement.Center
        ) {
            if (iconBitmap != null) {
                Image(
                    bitmap = iconBitmap,
                    contentDescription = command.name,
                    contentScale = ContentScale.Fit,
                    modifier = Modifier.size(34.dp)
                )
            } else {
                Text(text = fallbackEmoji, fontSize = 24.sp)
            }
            Spacer(modifier = Modifier.height(6.dp))
            Text(
                text = buttonLabel,
                fontSize = 13.sp,
                fontWeight = FontWeight.SemiBold,
                textAlign = TextAlign.Center,
                maxLines = 2,
                overflow = TextOverflow.Ellipsis
            )
        }
    }
}
