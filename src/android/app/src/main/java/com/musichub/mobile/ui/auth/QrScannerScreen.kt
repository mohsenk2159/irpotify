package com.musichub.mobile.ui.auth

import android.Manifest
import android.content.Context
import android.content.pm.PackageManager
import android.os.Build
import android.os.Handler
import android.os.Looper
import android.os.VibrationEffect
import android.os.Vibrator
import android.os.VibratorManager
import android.util.Log
import android.util.Size
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.camera.core.Camera
import androidx.camera.core.CameraSelector
import androidx.camera.core.ImageAnalysis
import androidx.camera.core.ImageProxy
import androidx.camera.core.Preview
import androidx.camera.lifecycle.ProcessCameraProvider
import androidx.camera.view.PreviewView
import androidx.compose.animation.core.LinearEasing
import androidx.compose.animation.core.RepeatMode
import androidx.compose.animation.core.animateFloat
import androidx.compose.animation.core.infiniteRepeatable
import androidx.compose.animation.core.rememberInfiniteTransition
import androidx.compose.animation.core.tween
import androidx.compose.foundation.Canvas
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.BoxWithConstraints
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
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
import androidx.compose.ui.geometry.CornerRadius
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.geometry.Rect
import androidx.compose.ui.geometry.RoundRect
import androidx.compose.ui.geometry.Size as GeometrySize
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.graphics.ClipOp
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.Path
import androidx.compose.ui.graphics.StrokeCap
import androidx.compose.ui.graphics.drawscope.Stroke
import androidx.compose.ui.graphics.drawscope.clipPath
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.res.painterResource
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.compose.ui.viewinterop.AndroidView
import androidx.core.content.ContextCompat
import androidx.lifecycle.compose.LocalLifecycleOwner
import com.google.zxing.BarcodeFormat
import com.google.zxing.BinaryBitmap
import com.google.zxing.DecodeHintType
import com.google.zxing.LuminanceSource
import com.google.zxing.MultiFormatReader
import com.google.zxing.NotFoundException
import com.google.zxing.PlanarYUVLuminanceSource
import com.google.zxing.common.GlobalHistogramBinarizer
import com.google.zxing.common.HybridBinarizer
import com.musichub.mobile.R
import kotlinx.coroutines.suspendCancellableCoroutine
import java.util.concurrent.Executors
import java.util.concurrent.atomic.AtomicBoolean
import kotlin.coroutines.resume
import kotlin.math.min

private const val TAG = "QrScanner"

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
    var cameraInstance by remember { mutableStateOf<Camera?>(null) }
    var hasFlash by remember { mutableStateOf(false) }
    var torchEnabled by remember { mutableStateOf(false) }

    val permissionLauncher = rememberLauncherForActivityResult(
        ActivityResultContracts.RequestPermission(),
    ) { allowed -> granted = allowed }

    LaunchedEffect(Unit) {
        if (!granted) permissionLauncher.launch(Manifest.permission.CAMERA)
    }

    val executor = remember { Executors.newSingleThreadExecutor() }
    val handled = remember { AtomicBoolean(false) }
    val mainHandler = remember { Handler(Looper.getMainLooper()) }
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
        } catch (exception: Exception) {
            Log.e(TAG, "Failed to get CameraProvider", exception)
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
                    Log.i(TAG, "QR detected: ${value.take(40)}...")
                    triggerHapticFeedback(context)
                    mainHandler.post {
                        onResult(value)
                    }
                }
            }
        }

        try {
            provider.unbindAll()
            val camera = provider.bindToLifecycle(
                lifecycleOwner,
                CameraSelector.DEFAULT_BACK_CAMERA,
                preview,
                analysis,
            )
            cameraInstance = camera
            hasFlash = camera.cameraInfo.hasFlashUnit()
        } catch (exception: Exception) {
            Log.e(TAG, "Failed to bind camera lifecycle", exception)
            cameraError = true
        }
    }

    // Viewfinder animations
    val infiniteTransition = rememberInfiniteTransition(label = "scanLaser")
    val laserProgress by infiniteTransition.animateFloat(
        initialValue = 0f,
        targetValue = 1f,
        animationSpec = infiniteRepeatable(
            animation = tween(durationMillis = 2200, easing = LinearEasing),
            repeatMode = RepeatMode.Reverse,
        ),
        label = "laserPosition",
    )

    Box(modifier = Modifier.fillMaxSize().background(Color.Black)) {
        if (granted && !cameraError) {
            AndroidView(
                factory = { previewView },
                modifier = Modifier.fillMaxSize(),
            )

            // Scanning reticle overlay
            BoxWithConstraints(modifier = Modifier.fillMaxSize()) {
                val density = LocalDensity.current
                val boxSizeDp = 270.dp
                val boxSizePx = with(density) { boxSizeDp.toPx() }
                val cornerRadiusPx = with(density) { 18.dp.toPx() }
                val cornerLengthPx = with(density) { 36.dp.toPx() }
                val cornerStrokePx = with(density) { 4.5.dp.toPx() }
                val primaryColor = MaterialTheme.colorScheme.primary
                val laserColor = MaterialTheme.colorScheme.tertiary.takeIf { it != Color.Unspecified } ?: Color(0xFF00E5FF)

                Canvas(modifier = Modifier.fillMaxSize()) {
                    val left = (size.width - boxSizePx) / 2f
                    val top = (size.height - boxSizePx) / 2f - with(density) { 30.dp.toPx() }
                    val right = left + boxSizePx
                    val bottom = top + boxSizePx

                    // Cutout mask (dark scrim around the reticle)
                    val roundRectPath = Path().apply {
                        addRoundRect(
                            RoundRect(
                                rect = Rect(left, top, right, bottom),
                                cornerRadius = CornerRadius(cornerRadiusPx, cornerRadiusPx),
                            ),
                        )
                    }

                    clipPath(roundRectPath, clipOp = ClipOp.Difference) {
                        drawRect(color = Color.Black.copy(alpha = 0.65f))
                    }

                    // 4 Corner Brackets
                    // Top-Left
                    val tlPath = Path().apply {
                        moveTo(left, top + cornerLengthPx)
                        lineTo(left, top + cornerRadiusPx)
                        quadraticTo(left, top, left + cornerRadiusPx, top)
                        lineTo(left + cornerLengthPx, top)
                    }
                    drawPath(tlPath, primaryColor, style = Stroke(width = cornerStrokePx, cap = StrokeCap.Round))

                    // Top-Right
                    val trPath = Path().apply {
                        moveTo(right - cornerLengthPx, top)
                        lineTo(right - cornerRadiusPx, top)
                        quadraticTo(right, top, right, top + cornerRadiusPx)
                        lineTo(right, top + cornerLengthPx)
                    }
                    drawPath(trPath, primaryColor, style = Stroke(width = cornerStrokePx, cap = StrokeCap.Round))

                    // Bottom-Left
                    val blPath = Path().apply {
                        moveTo(left, bottom - cornerLengthPx)
                        lineTo(left, bottom - cornerRadiusPx)
                        quadraticTo(left, bottom, left + cornerRadiusPx, bottom)
                        lineTo(left + cornerLengthPx, bottom)
                    }
                    drawPath(blPath, primaryColor, style = Stroke(width = cornerStrokePx, cap = StrokeCap.Round))

                    // Bottom-Right
                    val brPath = Path().apply {
                        moveTo(right - cornerLengthPx, bottom)
                        lineTo(right - cornerRadiusPx, bottom)
                        quadraticTo(right, bottom, right, bottom - cornerRadiusPx)
                        lineTo(right, bottom - cornerLengthPx)
                    }
                    drawPath(brPath, primaryColor, style = Stroke(width = cornerStrokePx, cap = StrokeCap.Round))

                    // Laser scan beam with glow
                    val laserY = top + (boxSizePx * laserProgress)
                    val laserBrush = Brush.horizontalGradient(
                        colors = listOf(
                            Color.Transparent,
                            laserColor.copy(alpha = 0.9f),
                            laserColor,
                            laserColor.copy(alpha = 0.9f),
                            Color.Transparent,
                        ),
                        startX = left + 8f,
                        endX = right - 8f,
                    )
                    drawLine(
                        brush = laserBrush,
                        start = Offset(left + 8f, laserY),
                        end = Offset(right - 8f, laserY),
                        strokeWidth = with(density) { 2.5.dp.toPx() },
                    )
                }
            }
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

        // Top Action Bar
        Row(
            modifier = Modifier
                .align(Alignment.TopCenter)
                .fillMaxWidth()
                .padding(horizontal = 16.dp, vertical = 20.dp),
            horizontalArrangement = Arrangement.SpaceBetween,
            verticalAlignment = Alignment.CenterVertically,
        ) {
            IconButton(
                onClick = onCancel,
                modifier = Modifier
                    .size(44.dp)
                    .background(Color.Black.copy(alpha = 0.55f), CircleShape),
            ) {
                Icon(
                    painter = painterResource(R.drawable.ic_close),
                    contentDescription = stringResource(R.string.action_close),
                    tint = Color.White,
                    modifier = Modifier.size(24.dp),
                )
            }

            if (hasFlash && cameraInstance != null) {
                IconButton(
                    onClick = {
                        val next = !torchEnabled
                        torchEnabled = next
                        cameraInstance?.cameraControl?.enableTorch(next)
                    },
                    modifier = Modifier
                        .size(44.dp)
                        .background(
                            if (torchEnabled) MaterialTheme.colorScheme.primary else Color.Black.copy(alpha = 0.55f),
                            CircleShape,
                        ),
                ) {
                    Icon(
                        painter = painterResource(if (torchEnabled) R.drawable.ic_flash_on else R.drawable.ic_flash_off),
                        contentDescription = "Flashlight",
                        tint = if (torchEnabled) Color.Black else Color.White,
                        modifier = Modifier.size(22.dp),
                    )
                }
            }
        }

        // Bottom Helper Texts
        Column(
            modifier = Modifier
                .align(Alignment.BottomCenter)
                .fillMaxWidth()
                .padding(horizontal = 24.dp, vertical = 40.dp),
            horizontalAlignment = Alignment.CenterHorizontally,
        ) {
            Text(
                stringResource(R.string.qr_scanning),
                color = Color.White,
                style = MaterialTheme.typography.titleMedium,
                fontWeight = FontWeight.SemiBold,
                textAlign = TextAlign.Center,
            )
            Spacer(Modifier.height(8.dp))
            Text(
                stringResource(R.string.pairing_scan_title),
                color = Color.White.copy(alpha = 0.75f),
                style = MaterialTheme.typography.bodyMedium,
                textAlign = TextAlign.Center,
            )
        }
    }
}

private suspend fun awaitCameraProvider(context: Context): ProcessCameraProvider =
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

private fun triggerHapticFeedback(context: Context) {
    try {
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.S) {
            val vibratorManager = context.getSystemService(Context.VIBRATOR_MANAGER_SERVICE) as? VibratorManager
            vibratorManager?.defaultVibrator?.vibrate(
                VibrationEffect.createOneShot(120, VibrationEffect.DEFAULT_AMPLITUDE),
            )
        } else {
            @Suppress("DEPRECATION")
            val vibrator = context.getSystemService(Context.VIBRATOR_SERVICE) as? Vibrator
            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
                vibrator?.vibrate(
                    VibrationEffect.createOneShot(120, VibrationEffect.DEFAULT_AMPLITUDE),
                )
            } else {
                @Suppress("DEPRECATION")
                vibrator?.vibrate(120)
            }
        }
    } catch (_: Exception) {}
}

private fun analyzeQr(proxy: ImageProxy, onResult: (String) -> Unit) {
    val image = proxy.image
    if (image == null) {
        proxy.close()
        return
    }
    try {
        val rotation = proxy.imageInfo.rotationDegrees
        val (rotatedData, rotatedSize) = extractAndRotateLuminance(image, rotation)
        val rotatedSource = PlanarYUVLuminanceSource(
            rotatedData,
            rotatedSize.width,
            rotatedSize.height,
            0,
            0,
            rotatedSize.width,
            rotatedSize.height,
            false,
        )

        val reader = MultiFormatReader().apply {
            setHints(
                mapOf(
                    DecodeHintType.POSSIBLE_FORMATS to listOf(BarcodeFormat.QR_CODE),
                    DecodeHintType.TRY_HARDER to true,
                    DecodeHintType.CHARACTER_SET to "UTF-8",
                ),
            )
        }

        // Try upright rotated source first
        var text = decodeWithFallbacks(reader, rotatedSource)

        // Fallback: If sensor rotation was uncompensated or different, try raw unrotated buffer
        if (text == null && rotation != 0) {
            val (rawData, rawSize) = extractAndRotateLuminance(image, 0)
            val rawSource = PlanarYUVLuminanceSource(
                rawData,
                rawSize.width,
                rawSize.height,
                0,
                0,
                rawSize.width,
                rawSize.height,
                false,
            )
            text = decodeWithFallbacks(reader, rawSource)
        }

        if (!text.isNullOrBlank()) {
            onResult(text)
        }
    } catch (e: Exception) {
        Log.w(TAG, "Frame decode error: ${e.message}")
    } finally {
        proxy.close()
    }
}

private fun decodeWithFallbacks(reader: MultiFormatReader, source: LuminanceSource): String? {
    // 1. Standard HybridBinarizer (best for contrast on physical prints)
    try {
        val result = reader.decodeWithState(BinaryBitmap(HybridBinarizer(source)))
        if (result.text.isNotBlank()) return result.text
    } catch (_: NotFoundException) {
    } finally {
        reader.reset()
    }

    // 2. GlobalHistogramBinarizer (best for screens/glare)
    try {
        val result = reader.decodeWithState(BinaryBitmap(GlobalHistogramBinarizer(source)))
        if (result.text.isNotBlank()) return result.text
    } catch (_: NotFoundException) {
    } finally {
        reader.reset()
    }

    // 3. Inverted luminance (dark mode white-on-black QR codes)
    try {
        val result = reader.decodeWithState(BinaryBitmap(HybridBinarizer(source.invert())))
        if (result.text.isNotBlank()) return result.text
    } catch (_: NotFoundException) {
    } finally {
        reader.reset()
    }

    return null
}

private fun extractAndRotateLuminance(
    image: android.media.Image,
    rotationDegrees: Int,
): Pair<ByteArray, Size> {
    val plane = image.planes[0]
    val buffer = plane.buffer
    val rowStride = plane.rowStride
    val width = image.width
    val height = image.height

    val yBytes = ByteArray(width * height)
    if (rowStride == width) {
        buffer.position(0)
        val toRead = min(yBytes.size, buffer.remaining())
        buffer.get(yBytes, 0, toRead)
    } else {
        for (y in 0 until height) {
            val offset = y * rowStride
            if (offset >= buffer.limit()) break
            buffer.position(offset)
            val toRead = min(width, buffer.remaining())
            buffer.get(yBytes, y * width, toRead)
        }
    }

    return when (rotationDegrees) {
        90 -> {
            val rotated = ByteArray(width * height)
            for (y in 0 until height) {
                val rowOffset = y * width
                for (x in 0 until width) {
                    rotated[x * height + (height - 1 - y)] = yBytes[rowOffset + x]
                }
            }
            Pair(rotated, Size(height, width))
        }
        180 -> {
            val rotated = ByteArray(width * height)
            val total = width * height
            for (i in 0 until total) {
                rotated[total - 1 - i] = yBytes[i]
            }
            Pair(rotated, Size(width, height))
        }
        270 -> {
            val rotated = ByteArray(width * height)
            for (y in 0 until height) {
                val rowOffset = y * width
                for (x in 0 until width) {
                    rotated[(width - 1 - x) * height + y] = yBytes[rowOffset + x]
                }
            }
            Pair(rotated, Size(height, width))
        }
        else -> {
            Pair(yBytes, Size(width, height))
        }
    }
}
