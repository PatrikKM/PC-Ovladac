package cz.pcovladac.app.data

import java.text.Normalizer

object VoiceCommandMatcher {

    fun matchCommand(spokenText: String, availableCommands: List<PcCommand>): String {
        val norm = normalize(spokenText)
        if (norm.isBlank()) return ""

        // 1. Přesná nebo podřetězcová shoda s názvy načtených příkazů z PC
        for (cmd in availableCommands) {
            val cmdNorm = normalize(cmd.name.replace('_', ' '))
            val dispNorm = normalize(cmd.displayName)
            if (norm == cmdNorm || norm == normalize(cmd.name) || norm.contains(cmdNorm) || norm == dispNorm) {
                return cmd.name
            }
        }

        // 2. Běžné české hlasové fráze -> výchozí příkazy
        val synonyms = listOf(
            listOf("nahoru", "zesil", "pridej", "hlasiteji", "vic zvuku", "zvys") to "hlasitost_nahoru",
            listOf("dolu", "ztis", "uber", "tiseji", "min zvuku", "sniz") to "hlasitost_dolu",
            listOf("ztlum", "ticho", "mute", "vypni zvuk", "zrus zvuk") to "ztlumit",
            listOf("pauza", "zastav", "pust", "prehraj", "play", "stop") to "pauza",
            listOf("dalsi", "preskoc", "next", "nasledujici") to "dalsi",
            listOf("predchozi", "zpet", "minul", "prev") to "predchozi",
            listOf("f11", "f 11", "cela obrazovka", "celou obrazovku", "fullscreen", "na celou") to "f11",
            listOf("alt tab", "alttab", "prepni okno", "prepnout okno", "dalsi okno", "jine okno") to "alt_tab",
            listOf("zamkni", "zamknout", "uzamkni") to "zamknout",
            listOf("uspi", "uspat", "spanek") to "uspat",
            listOf("vypni pocitac", "vypnout", "vypni pc") to "vypnout"
        )

        for ((keywords, targetCmd) in synonyms) {
            if (keywords.any { norm.contains(it) }) {
                val existing = availableCommands.firstOrNull {
                    it.name.equals(targetCmd, ignoreCase = true) ||
                        (targetCmd == "alt_tab" && it.name.equals("tab", ignoreCase = true)) ||
                        (targetCmd == "f11" && it.name.equals("obrazovka", ignoreCase = true))
                }
                return existing?.name ?: targetCmd
            }
        }

        // 3. Fallback: odešli normalizované první/celé slovo jako název vlastního příkazu
        return norm.replace(' ', '_')
    }

    private fun normalize(text: String): String {
        val decomposed = Normalizer.normalize(text.trim().lowercase(), Normalizer.Form.NFD)
        return decomposed
            .replace(Regex("\\p{InCombiningDiacriticalMarks}+"), "")
            .replace(Regex("[^a-z0-9_ ]"), "")
            .replace(Regex("\\s+"), " ")
            .trim()
    }
}
