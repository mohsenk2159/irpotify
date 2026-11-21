package com.musichub.mobile

import java.io.File
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertTrue
import org.junit.Test

/**
 * Guards the launch theme wiring.
 *
 * The app is entirely Compose. If AndroidManifest.xml declares no android:theme, the platform
 * default theme supplies an ActionBar, which draws a second title bar over the Compose content:
 * the Search field and the Home header were rendered underneath it on device
 * (android:id/action_bar_container = [0,107][1080,254] while Compose content started at y=147).
 *
 * A Compose UI test cannot catch this because the offending view is created by the platform,
 * outside the composition, so the assertion is made against the source resources instead.
 */
class ManifestThemeTest {

    // Gradle runs JVM unit tests with the module directory as the working directory.
    private fun moduleFile(relative: String): File {
        val direct = File(relative)
        if (direct.isFile) return direct
        // Fall back to resolving from the build directory for non-standard working dirs.
        val fromBuild = File("app/$relative")
        if (fromBuild.isFile) return fromBuild
        return direct
    }

    @Test
    fun `application declares the MusicHub theme`() {
        val manifest = moduleFile("src/main/AndroidManifest.xml")
        assertTrue("AndroidManifest.xml not found at ${manifest.absolutePath}", manifest.isFile)

        val text = manifest.readText()
        assertTrue(
            "AndroidManifest.xml must declare android:theme so the platform does not add an ActionBar",
            Regex("""android:theme\s*=\s*"@style/Theme\.MusicHub"""").containsMatchIn(text),
        )
    }

    @Test
    fun `declared theme is a NoActionBar theme`() {
        val themes = moduleFile("src/main/res/values/themes.xml")
        assertTrue("themes.xml not found at ${themes.absolutePath}", themes.isFile)

        val text = themes.readText()
        val match = Regex("""<style\s+name="Theme\.MusicHub"\s+parent="([^"]+)"""").find(text)
        assertNotNull("Theme.MusicHub must be defined in res/values/themes.xml", match)

        val parent = match!!.groupValues[1]
        assertTrue(
            "Theme.MusicHub parent must be a NoActionBar theme, was '$parent'",
            parent.contains("NoActionBar"),
        )
    }
}
