package com.musichub.mobile.ui

import android.content.Context
import android.content.res.Configuration
import java.util.Locale

/**
 * Lightweight per-app locale override. The selected tag is mirrored into normal SharedPreferences
 * so attachBaseContext can apply it synchronously before Compose resources are resolved.
 */
object LocaleHelper {
    private const val PREFS = "musichub_locale"
    private const val KEY_TAG = "language_tag"

    fun currentTag(context: Context): String =
        context.getSharedPreferences(PREFS, Context.MODE_PRIVATE).getString(KEY_TAG, "").orEmpty()

    fun setLanguage(context: Context, tag: String) {
        context.getSharedPreferences(PREFS, Context.MODE_PRIVATE)
            .edit()
            .putString(KEY_TAG, tag)
            .apply()
    }

    fun wrap(base: Context): Context {
        val tag = currentTag(base)
        if (tag.isBlank()) return base
        val locale = Locale.forLanguageTag(tag)
        Locale.setDefault(locale)
        val configuration = Configuration(base.resources.configuration)
        configuration.setLocale(locale)
        configuration.setLayoutDirection(locale)
        return base.createConfigurationContext(configuration)
    }
}
