# Keep serialization metadata for API models.
-keepattributes *Annotation*, InnerClasses, Signature, RuntimeVisibleAnnotations
-dontnote kotlinx.serialization.**
-keepclassmembers class **$$serializer { *; }
-keepclasseswithmembers class * {
    kotlinx.serialization.KSerializer serializer(...);
}

# kotlinx.serialization generates a companion `serializer()` and reflective accessors. Without
# these the release build throws SerializationException at runtime on the first API response,
# because R8 renames or strips the generated members. The debug build never showed it.
-keepclassmembers class kotlinx.serialization.json.** {
    *** Companion;
}
-keepclasseswithmembers class kotlinx.serialization.json.** {
    kotlinx.serialization.KSerializer serializer(...);
}
-keep,includedescriptorclasses class com.musichub.mobile.**$$serializer { *; }
-keepclassmembers class com.musichub.mobile.** {
    *** Companion;
}
-keepclasseswithmembers class com.musichub.mobile.** {
    kotlinx.serialization.KSerializer serializer(...);
}

# OkHttp and Retrofit reflect over annotations and generic signatures.
-dontwarn okhttp3.**
-dontwarn okio.**
-dontwarn retrofit2.**
-keepattributes Exceptions, *Annotation*
-keepclassmembers,allowshrinking,allowobfuscation interface * {
    @retrofit2.http.* <methods>;
}

# Media3 / ExoPlayer resolves extractors and renderers by name.
-keep class androidx.media3.** { *; }
-dontwarn androidx.media3.**

# Room generates implementations referenced only from generated code.
-keep class * extends androidx.room.RoomDatabase
-dontwarn androidx.room.paging.**

# WorkManager instantiates workers by class name from its own database.
-keep class * extends androidx.work.ListenableWorker { *; }
-keep class * extends androidx.work.Worker { *; }
-keep class * extends androidx.work.CoroutineWorker { *; }

# Keep the app entry points and anything Compose reflects on.
-keep class com.musichub.mobile.MusicHubApp { *; }
-keep class com.musichub.mobile.MainActivity { *; }
-keep class com.musichub.mobile.playback.PlaybackService { *; }

# Do not strip line numbers: crash reports from a release build must stay actionable.
-keepattributes SourceFile,LineNumberTable
-renamesourcefileattribute SourceFile
