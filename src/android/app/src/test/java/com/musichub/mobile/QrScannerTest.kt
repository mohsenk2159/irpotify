package com.musichub.mobile

import com.google.zxing.BarcodeFormat
import com.google.zxing.BinaryBitmap
import com.google.zxing.DecodeHintType
import com.google.zxing.LuminanceSource
import com.google.zxing.MultiFormatReader
import com.google.zxing.NotFoundException
import com.google.zxing.PlanarYUVLuminanceSource
import com.google.zxing.common.GlobalHistogramBinarizer
import com.google.zxing.common.HybridBinarizer
import com.google.zxing.qrcode.QRCodeWriter
import com.musichub.mobile.core.PairingParseResult
import com.musichub.mobile.core.PairingPayloadParser
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertTrue
import org.junit.Test

class QrScannerTest {

    private fun rotateY(data: ByteArray, width: Int, height: Int, rotationDegrees: Int): Pair<ByteArray, Pair<Int, Int>> {
        return when (rotationDegrees) {
            90 -> {
                val rotated = ByteArray(width * height)
                for (y in 0 until height) {
                    val rowOffset = y * width
                    for (x in 0 until width) {
                        rotated[x * height + (height - 1 - y)] = data[rowOffset + x]
                    }
                }
                Pair(rotated, Pair(height, width))
            }
            180 -> {
                val rotated = ByteArray(width * height)
                val total = width * height
                for (i in 0 until total) {
                    rotated[total - 1 - i] = data[i]
                }
                Pair(rotated, Pair(width, height))
            }
            270 -> {
                val rotated = ByteArray(width * height)
                for (y in 0 until height) {
                    val rowOffset = y * width
                    for (x in 0 until width) {
                        rotated[(width - 1 - x) * height + y] = data[rowOffset + x]
                    }
                }
                Pair(rotated, Pair(height, width))
            }
            else -> Pair(data, Pair(width, height))
        }
    }

    private fun decode(data: ByteArray, width: Int, height: Int): String? {
        val reader = MultiFormatReader().apply {
            setHints(
                mapOf(
                    DecodeHintType.POSSIBLE_FORMATS to listOf(BarcodeFormat.QR_CODE),
                    DecodeHintType.TRY_HARDER to true,
                    DecodeHintType.CHARACTER_SET to "UTF-8",
                ),
            )
        }
        val source = PlanarYUVLuminanceSource(data, width, height, 0, 0, width, height, false)
        try {
            return reader.decodeWithState(BinaryBitmap(HybridBinarizer(source))).text
        } catch (_: NotFoundException) {}
        finally { reader.reset() }

        try {
            return reader.decodeWithState(BinaryBitmap(GlobalHistogramBinarizer(source))).text
        } catch (_: NotFoundException) {}
        finally { reader.reset() }

        return null
    }

    @Test
    fun testQrEncodingAndDecodingWithRotation() {
        val payload = "musichub://pair?server=http%3A%2F%2F31.18.73.34%3A4590&code=rXpSthlsSUJwq15Lb_pqXA&expires=1797967075&lan=http%3A%2F%2F172.18.0.3%3A5080&device=Mohsen%20Karami%27s%20Android%20Device&email=mohsenk2159%40gmail.com&name=Mohsen%20Karami"
        val qrMatrix = QRCodeWriter().encode(payload, BarcodeFormat.QR_CODE, 200, 200)
        val width = qrMatrix.width
        val height = qrMatrix.height
        val rawLuminance = ByteArray(width * height)
        for (y in 0 until height) {
            for (x in 0 until width) {
                rawLuminance[y * width + x] = if (qrMatrix.get(x, y)) 0.toByte() else 255.toByte()
            }
        }

        // 0 degrees
        val decoded0 = decode(rawLuminance, width, height)
        assertEquals(payload, decoded0)

        // 90 degrees clockwise
        val (rotated90, size90) = rotateY(rawLuminance, width, height, 90)
        val decoded90 = decode(rotated90, size90.first, size90.second)
        assertEquals(payload, decoded90)

        // 180 degrees
        val (rotated180, size180) = rotateY(rawLuminance, width, height, 180)
        val decoded180 = decode(rotated180, size180.first, size180.second)
        assertEquals(payload, decoded180)

        // 270 degrees
        val (rotated270, size270) = rotateY(rawLuminance, width, height, 270)
        val decoded270 = decode(rotated270, size270.first, size270.second)
        assertEquals(payload, decoded270)
    }

    @Test
    fun testPairingPayloadParser() {
        val raw = "musichub://pair?server=http%3A%2F%2F31.18.73.34%3A4590&code=rXpSthlsSUJwq15Lb_pqXA&expires=1797967075&lan=http%3A%2F%2F172.18.0.3%3A5080&device=Mohsen%20Karami%27s%20Android%20Device&email=mohsenk2159%40gmail.com&name=Mohsen%20Karami"
        val result = PairingPayloadParser.parse(raw)
        assertTrue(result is PairingParseResult.Success)
        val payload = (result as PairingParseResult.Success).payload
        assertEquals("http://31.18.73.34:4590", payload.serverUrl)
        assertEquals("http://172.18.0.3:5080", payload.lanServerUrl)
        assertEquals("rXpSthlsSUJwq15Lb_pqXA", payload.code)
        assertEquals("Mohsen Karami's Android Device", payload.deviceName)
        assertEquals("mohsenk2159@gmail.com", payload.userEmail)

        // Irpotify scheme
        val rawIrpotify = "irpotify://pair?server=https%3A%2F%2Fmusic.example.com&code=abc123xyz"
        val resultIrpotify = PairingPayloadParser.parse(rawIrpotify)
        assertTrue(resultIrpotify is PairingParseResult.Success)
        val payloadIrpotify = (resultIrpotify as PairingParseResult.Success).payload
        assertEquals("https://music.example.com", payloadIrpotify.serverUrl)
        assertEquals("abc123xyz", payloadIrpotify.code)
    }
}
