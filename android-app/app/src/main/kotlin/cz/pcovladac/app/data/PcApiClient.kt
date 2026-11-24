package cz.pcovladac.app.data

import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import org.json.JSONObject
import java.io.BufferedReader
import java.io.InputStreamReader
import java.net.HttpURLConnection
import java.net.URL
import java.net.URLEncoder

data class PcCommand(
    val name: String,
    val actionType: String,
    val displayName: String,
    val iconBase64: String = ""
)

object PcApiClient {

    val defaultCommands: List<PcCommand> = listOf(
        PcCommand("hlasitost_nahoru", "VolumeUp", "Zvýšit hlasitost"),
        PcCommand("hlasitost_dolu", "VolumeDown", "Snížit hlasitost"),
        PcCommand("ztlumit", "VolumeMute", "Ztlumit zvuk"),
        PcCommand("pauza", "MediaPlayPause", "Přehrát / Pauza"),
        PcCommand("dalsi", "MediaNext", "Další skladba"),
        PcCommand("predchozi", "MediaPrevious", "Předchozí skladba"),
        PcCommand("zamknout", "LockScreen", "Zamknout PC"),
        PcCommand("f11", "Shortcut", "Celá obrazovka (F11)"),
        PcCommand("alt_tab", "Shortcut", "Přepnout okno (Alt+Tab)")
    )

    suspend fun fetchCommands(host: String, port: Int): Result<List<PcCommand>> = withContext(Dispatchers.IO) {
        try {
            val response = httpGet("http://$host:$port/commands")
            val root = JSONObject(response.body)
            val arr = root.optJSONArray("commands")
            val result = mutableListOf<PcCommand>()
            if (arr != null) {
                for (i in 0 until arr.length()) {
                    val obj = arr.getJSONObject(i)
                    result.add(
                        PcCommand(
                            name = obj.optString("name", ""),
                            actionType = obj.optString("actionType", ""),
                            displayName = obj.optString("displayName", obj.optString("name", "")),
                            iconBase64 = obj.optString("iconBase64", "")
                        )
                    )
                }
            }
            Result.success(if (result.isEmpty()) defaultCommands else result)
        } catch (e: Exception) {
            Result.failure(e)
        }
    }

    suspend fun sendCommand(host: String, port: Int, commandName: String): Result<String> = withContext(Dispatchers.IO) {
        try {
            val encoded = URLEncoder.encode(commandName, "UTF-8").replace("+", "%20")
            val response = httpGet("http://$host:$port/cmd/$encoded")
            val json = try { JSONObject(response.body) } catch (_: Exception) { null }
            val message = json?.optString("message")?.takeIf { it.isNotBlank() }
                ?: if (response.code in 200..299) "Příkaz '$commandName' byl proveden." else "Chyba HTTP ${response.code}"

            if (response.code in 200..299 && json?.optString("status") != "error") {
                Result.success(message)
            } else {
                Result.failure(Exception(message))
            }
        } catch (e: Exception) {
            Result.failure(e)
        }
    }

    private data class HttpResult(val code: Int, val body: String)

    private fun httpGet(urlString: String): HttpResult {
        val conn = (URL(urlString).openConnection() as HttpURLConnection).apply {
            requestMethod = "GET"
            connectTimeout = 3500
            readTimeout = 4500
            setRequestProperty("Accept", "application/json")
            setRequestProperty("Connection", "close")
        }
        try {
            val code = conn.responseCode
            val stream = if (code in 200..299) conn.inputStream else conn.errorStream
            val body = if (stream != null) {
                BufferedReader(InputStreamReader(stream, Charsets.UTF_8)).use { it.readText() }
            } else {
                ""
            }
            return HttpResult(code, body)
        } finally {
            conn.disconnect()
        }
    }
}
