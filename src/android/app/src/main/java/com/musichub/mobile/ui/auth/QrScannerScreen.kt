package com.musichub.mobile.ui.auth

import android.Manifest
import android.content.pm.PackageManager
import android.util.Size
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.camera.core.CameraSelector
import androidx.camera.core.ImageAnalysis
import androidx.camera.core.ImageProxy
import androidx.camera.core.Preview
import androidx.camera.lifecycle.ProcessCameraProvider
import androidx.camera.view.PreviewView
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.material3.Button
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.DisposableEffect
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.res.painterResource
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import androidx.compose.ui.viewinterop.AndroidView
import androidx.core.content.ContextCompat
import androidx.lifecycle.compose.LocalLifecycleOwner
import com.google.zxing.BarcodeFormat
import com.google.zxing.BinaryBitmap
import com.google.zxing.DecodeHintType
import com.google.zxing.MultiFormatReader
import com.google.zxing.NotFoundException
import com.google.zxing.PlanarYUVLuminanceSource
import com.google.zxing.common.HybridBinarizer
import com.musichub.mobile.R
import kotlinx.coroutines.suspendCancellableCoroutine
import java.util.concurrent.Executors
import java.util.concurrent.atomic.AtomicBoolean
import kotlin.coroutines.resume
import kotlin.math.min

@Composable
fun QrScannerScreen(
    onResult: (String) -> Unit,
    onCancel: () -> Unit,
) {
    val context = LocalContext.current
    val lifecycleOwner = LocalLifecycleOwner.current
    var granted by remember {
        mutableStateOf(
            ContextCompat.checkSelfPermission(context, Manifest.permission.CAMERA) ==
                PackageManager.PERMISSION_GRANTED,
        )
    }
    var cameraError by remember { mutableStateOf(false) }
    val permissionLauncher = rememberLauncherForActivityResult(
        ActivityResultContracts.RequestPermission(),
    ) { allowed -> granted = allowed }

    LaunchedEffect(Unit) {
        if (!granted) permissionLauncher.launch(Manifest.permission.CAMERA)
    }

    val executor = remember { Executors.newSingleThreadExecutor() }
    val handled = remember { AtomicBoolean(false) }
    val previewView = remember {
        PreviewView(context).apply {
            scaleType = PreviewView.ScaleType.FILL_CENTER
            implementationMode = PreviewView.ImplementationMode.COMPATIBLE
        }
    }

    DisposableEffect(Unit) {
        onDispose {
            runCatching { ProcessCameraProvider.getInstance(context).get().unbindAll() }
            executor.shutdown()
        }
    }

    LaunchedEffect(granted) {
        if (!granted) return@LaunchedEffect
        val provider = try {
            awaitCameraProvider(context)
        } catch (_: Exception) {
            cameraError = true
            return@LaunchedEffect
        }
        val preview = Preview.Builder().build().also {
            it.setSurfaceProvider(previewView.surfaceProvider)
        }
        val analysis = ImageAnalysis.Builder()
            .setTargetResolution(Size(1280, 720))
            .setBackpressureStrategy(ImageAnalysis.STRATEGY_KEEP_ONLY_LATEST)
            .build()
        analysis.setAnalyzer(executor) { proxy ->
            analyzeQr(proxy) { value ->
                if (handled.compareAndSet(false, true)) {
                    onResult(value)
                }
            }
        }
        try {
            provider.unbindAll()
            provider.bindToLifecycle(
                lifecycleOwner,
                CameraSelector.DEFAULT_BACK_CAMERA,
                preview,
                analysis,
            )
        } catch (_: Exception) {
            cameraError = true
        }
    }

    Box(modifier = Modifier.fillMaxSize().background(Color.Black)) {
        if (granted && !cameraError) {
            AndroidView(
                factory = { previewView },
                modifier = Modifier.fillMaxSize(),
            )
        }
        if (!granted) {
            Column(
                modifier = Modifier
                    .align(Alignment.Center)
                    .padding(32.dp),
                horizontalAlignment = Alignment.CenterHorizontally,
                verticalArrangement = Arrangement.spacedBy(12.dp),
            ) {
                Text(
                    stringResource(R.string.qr_permission_title),
                    color = Color.White,
                    style = MaterialTheme.typography.titleLarge,
                    textAlign = TextAlign.Center,
                )
                Text(
                    stringResource(R.string.qr_permission_body),
                    color = Color.White.copy(alpha = 0.8f),
                    textAlign = TextAlign.Center,
                )
                Button(onClick = { permissionLauncher.launch(Manifest.permission.CAMERA) }) {
                    Text(stringResource(R.string.qr_permission_button))
                }
            }
        }
        if (cameraError) {
            Text(
                stringResource(R.string.qr_camera_unavailable),
                color = Color.White,
                modifier = Modifier.align(Alignment.Center).padding(32.dp),
                textAlign = TextAlign.Center,
            )
        }

        IconButton(
            onClick = onCancel,
            modifier = Modifier
                .align(Alignment.TopStart)
                .padding(16.dp)
                .background(Color.Black.copy(alpha = 0.45f), CircleShape),
        ) {
            Icon(
                painterResource(android.R.drawable.ic_menu_close_clear_cancel),
                contentDescription = stringResource(R.string.action_close),
                tint = Color.White,
            )
        }
        Column(
            modifier = Modifier
                .align(Alignment.BottomCenter)
                .fillMaxWidth()
                .padding(24.dp),
            horizontalAlignment = Alignment.CenterHorizontally,
        ) {
            Text(
                stringResource(R.string.qr_scanning),
                color = Color.White,
                textAlign = TextAlign.Center,
            )
            Spacer(Modifier.height(8.dp))
            Text(
                stringResource(R.string.pairing_scan_title),
                color = Color.White.copy(alpha = 0.7f),
                style = MaterialTheme.typography.bodySmall,
            )
        }
    }
}

private suspend fun awaitCameraProvider(context: android.content.Context): ProcessCameraProvider =
    suspendCancellableCoroutine { continuation ->
        val future = ProcessCameraProvider.getInstance(context)
        future.addListener(
            {
                try {
                    if (continuation.isActive) continuation.resume(future.get())
                } catch (exception: Exception) {
                    continuation.cancel(exception)
                }
            },
            ContextCompat.getMainExecutor(context),
        )
    }

private fun analyzeQr(proxy: ImageProxy, onResult: (String) -> Unit) {
    val image = proxy.image
    if (image == null) {
        proxy.close()
        return
    }
    try {
        val data = extractLuminance(image)
        val width = image.width
        val height = image.height
        val base = PlanarYUVLuminanceSource(data, width, height, 0, 0, width, height, false)
        val candidates = when (proxy.imageInfo.rotationDegrees) {
            90 -> listOf(base, base.rotateCounterClockwise(), base.rotateCounterClockwise().rotateCounterClockwise())
            180 -> listOf(base, base.rotateCounterClockwise().rotateCounterClockwise())
            270 -> listOf(base, base.rotateCounterClockwise(), base.rotateCounterClockwise().rotateCounterClockwise(), base.rotateCounterClockwise().rotateCounterClockwise().rotateCounterClockwise())
            else -> listOf(base)
        }
        val reader = MultiFormatReader().apply {
            setHints(
                mapOf(
                    DecodeHintType.POSSIBLE_FORMATS to listOf(BarcodeFormat.QR_CODE),
                    DecodeHintType.TRY_HARDER to true,
                ),
            )
        }
        for (candidate in candidates) {
            try {
                val result = reader.decodeWithState(BinaryBitmap(HybridBinarizer(candidate)))
                if (result.text.isNotBlank()) {
                    onResult(result.text)
                    return
                }
            } catch (_: NotFoundException) {
                // Try the next rotation.
            } finally {
                reader.reset()
            }
        }
    } catch (_: Exception) {
        // Malformed frame; ignore.
    } finally {
        proxy.close()
    }
}

private fun extractLuminance(image: android.media.Image): ByteArray {
    val plane = image.planes[0]
    val buffer = plane.buffer
    val rowStride = plane.rowStride
    val pixelStride = plane.pixelStride
    val width = image.width
    val height = image.height
    val data = ByteArray(width * height)
    val row = ByteArray(rowStride)
    var output = 0
    for (y in 0 until height) {
        val rowStart = y * rowStride
        buffer.position(rowStart.coerceAtMost(buffer.limit()))
        val toRead = min(rowStride, buffer.remaining())
        if (toRead <= 0) break
        buffer.get(row, 0, toRead)
        var x = 0
        while (x < width) {
            val index = x * pixelStride
            data[output++] = if (index < toRead) row[index] else 0
            x++
        }
    }
    return data
}
