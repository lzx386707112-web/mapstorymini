package ms282;

import java.io.*;
import java.nio.ByteBuffer;
import java.nio.ByteOrder;
import java.nio.charset.StandardCharsets;
import java.nio.file.*;
import java.util.*;

/**
 * 独立 .ms 解包器（移植自 orange-wz OrzRepacker provider/ms/*，去 lombok/spring 依赖）。
 * 用法:
 *   java ms282.MsUnpack <file.ms> list
 *   java ms282.MsUnpack <file.ms> list <nameSubstr>
 *   java ms282.MsUnpack <file.ms> extract <outDir> <entryName>...
 *   java ms282.MsUnpack <file.ms> extractall <outDir> <nameSubstr>
 * 密码学逻辑与 orange-wz 完全一致（Snow2 v2 优先，ChaCha20 v4 兜底）。
 */
public final class MsUnpack {

    // ---------- WzMsConstants ----------
    static final int SNOW_KEY_LENGTH = 16;
    static final int BLOCK_ALIGNMENT = 1024;
    static final int PAGE_ALIGNMENT_MASK = 0x3FF;
    static final int RAND_BYTE_MOD = 312;
    static final int RAND_BYTE_OFFSET = 30;
    static final int HEADER_PAD_MOD = 212;
    static final long INITIAL_KEY_HASH = 0x811C9DC5L;
    static final long KEY_HASH_MULTIPLIER = 0x1000193L;
    static final int DOUBLE_ENCRYPT_INITIAL_BYTES = 1024;
    static final int ENTRY_KEY_SIZE = 16;

    static final int CHACHA_VERSION = 4;
    static final int CHACHA_HEADER_CIPHER_LEN = 8;
    static final int CHACHA_HEADER_PAD_OFFSET = 64;
    static final int CHACHA_KEY_LENGTH = 32;
    static final byte[] CHACHA20_KEY_OBSCURE = {
            0x7B, 0x2F, 0x35, 0x48, 0x43, (byte) 0x95, 0x02, (byte) 0xB9,
            (byte) 0xAE, (byte) 0x91, (byte) 0xA6, (byte) 0xE1, (byte) 0xD8, (byte) 0xD6, 0x24, (byte) 0xB4,
            0x33, 0x10, 0x1D, 0x3D, (byte) 0xC1, (byte) 0xBB, (byte) 0xC6, (byte) 0xF4,
            (byte) 0xA5, (byte) 0xFE, (byte) 0xB3, 0x69, 0x6B, 0x56, (byte) 0xE4, 0x75
    };

    // ---------- Entry ----------
    static final class Entry {
        String name; int checksum, flags, startPos, size, sizeAligned, unk1, unk2, unk3, unk4;
        byte[] entryKey;
    }

    static final class MsFile {
        String salt;         // null for chacha? no: both have salt
        boolean chacha;
        List<Entry> entries = new ArrayList<>();
        int dataStart;
        byte[] all;
        String fileNameLower;
    }

    public static void main(String[] args) throws Exception {
        if (args.length < 2) { usage(); return; }
        Path msPath = Paths.get(args[0]);
        String cmd = args[1];
        MsFile f = load(msPath);
        System.err.printf("# %s: format=%s entries=%d salt=%s dataStart=%d%n",
                msPath.getFileName(), f.chacha ? "ChaCha20-v4" : "Snow2-v2", f.entries.size(), f.salt, f.dataStart);
        switch (cmd) {
            case "list": {
                String sub = args.length > 2 ? args[2] : null;
                for (Entry e : f.entries) {
                    if (sub == null || e.name.contains(sub))
                        System.out.printf("%s\tsize=%d\taligned=%d\tflags=%d%n", e.name, e.size, e.sizeAligned, e.flags);
                }
                break;
            }
            case "extract": {
                Path outDir = Paths.get(args[2]);
                Files.createDirectories(outDir);
                Set<String> want = new HashSet<>(Arrays.asList(args).subList(3, args.length));
                for (Entry e : f.entries) {
                    if (want.contains(e.name)) {
                        byte[] raw = decryptEntry(f, e);
                        Path out = outDir.resolve(e.name.replace('/', '_'));
                        Files.write(out, raw);
                        System.out.printf("%s -> %s (%d bytes)%n", e.name, out, raw.length);
                    }
                }
                break;
            }
            case "extractall": {
                Path outDir = Paths.get(args[2]);
                Files.createDirectories(outDir);
                String sub = args[3];
                for (Entry e : f.entries) {
                    if (e.name.contains(sub)) {
                        byte[] raw = decryptEntry(f, e);
                        Path out = outDir.resolve(e.name.replace('/', '_'));
                        Files.write(out, raw);
                        System.out.printf("%s -> %s (%d bytes)%n", e.name, out, raw.length);
                    }
                }
                break;
            }
            default: usage();
        }
    }

    static void usage() {
        System.err.println("usage: MsUnpack <file.ms> list [substr] | extract <outDir> <name>... | extractall <outDir> <substr>");
    }

    // ---------- load ----------
    static MsFile load(Path msPath) throws IOException {
        byte[] all = Files.readAllBytes(msPath);
        String fileName = msPath.getFileName().toString().toLowerCase(Locale.ROOT);
        try {
            return loadSnowV2(all, fileName);
        } catch (Exception e) {
            System.err.println("# Snow2 miss: " + e.getMessage());
        }
        return loadChaCha(all, fileName);
    }

    static int charSum(String s) { int sum = 0; for (int i = 0; i < s.length(); i++) sum += s.charAt(i); return sum; }
    static int tripleSum(String s) { int sum = 0; for (int i = 0; i < s.length(); i++) sum += s.charAt(i) * 3; return sum; }
    static int align4(int v) { return (v + 3) & ~3; }

    // ---------- Snow2 v2 ----------
    static MsFile loadSnowV2(byte[] all, String fileName) {
        ByteBuffer bb = ByteBuffer.wrap(all).order(ByteOrder.LITTLE_ENDIAN);
        int randByteCount = charSum(fileName) % RAND_BYTE_MOD + RAND_BYTE_OFFSET;
        byte[] randBytes = new byte[randByteCount];
        bb.get(randBytes);

        int hashedSaltLen = bb.getInt();
        int saltLen = (hashedSaltLen & 0xFF) ^ (randBytes[0] & 0xFF);
        if (saltLen <= 0 || saltLen > randByteCount) throw new RuntimeException("bad saltLen " + saltLen);
        byte[] saltBytes = new byte[saltLen * 2];
        bb.get(saltBytes);
        char[] saltChars = new char[saltLen];
        for (int i = 0; i < saltLen; i++)
            saltChars[i] = (char) ((randBytes[i] & 0xFF) ^ (saltBytes[i * 2] & 0xFF));
        String salt = new String(saltChars);
        String fileNameWithSalt = fileName + salt;

        int headerStart = bb.position();
        int padAmount = tripleSum(fileName) % HEADER_PAD_MOD + 33;
        byte[] headerCipher = Arrays.copyOfRange(all, headerStart, headerStart + 9);
        byte[] headerPlain = new Snow2(deriveSnowHeaderKey(fileNameWithSalt), false).transform(headerCipher);
        ByteBuffer hb = ByteBuffer.wrap(headerPlain).order(ByteOrder.LITTLE_ENDIAN);
        int hash = hb.getInt();
        int version = hb.get() & 0xFF;
        int entryCount = hb.getInt();
        if (version != 2) throw new RuntimeException("version " + version);

        int checkHash = hashedSaltLen + version + entryCount;
        ByteBuffer sbuf = ByteBuffer.wrap(saltBytes).order(ByteOrder.LITTLE_ENDIAN);
        for (int i = 0; i < saltLen; i++) checkHash += sbuf.getShort() & 0xFFFF;
        if (checkHash != hash) throw new RuntimeException("header hash mismatch");

        int entryStart = headerStart + 9 + padAmount;
        // 解密整个尾部一次（Java 速度足够）
        byte[] tail = Arrays.copyOfRange(all, entryStart, all.length);
        byte[] tailPlain = new Snow2(deriveSnowEntryKey(fileNameWithSalt), false).transform(tail);
        ByteBuffer eb = ByteBuffer.wrap(tailPlain).order(ByteOrder.LITTLE_ENDIAN);

        MsFile f = new MsFile();
        f.all = all; f.fileNameLower = fileName; f.salt = salt; f.chacha = false;
        for (int i = 0; i < entryCount; i++) {
            Entry e = new Entry();
            int nameLen = eb.getInt();
            char[] chars = new char[nameLen];
            for (int j = 0; j < nameLen; j++) chars[j] = eb.getChar();
            e.name = new String(chars);
            e.checksum = eb.getInt(); e.flags = eb.getInt(); e.startPos = eb.getInt();
            e.size = eb.getInt(); e.sizeAligned = eb.getInt(); e.unk1 = eb.getInt(); e.unk2 = eb.getInt();
            e.entryKey = new byte[ENTRY_KEY_SIZE];
            eb.get(e.entryKey);
            f.entries.add(e);
        }
        int tableEnd = entryStart + eb.position();
        f.dataStart = (tableEnd + PAGE_ALIGNMENT_MASK) & ~PAGE_ALIGNMENT_MASK;
        return f;
    }

    static byte[] deriveSnowHeaderKey(String fnws) {
        byte[] key = new byte[SNOW_KEY_LENGTH];
        int len = fnws.length();
        for (int i = 0; i < key.length; i++) key[i] = (byte) (fnws.charAt(i % len) + i);
        return key;
    }

    static byte[] deriveSnowEntryKey(String fnws) {
        byte[] key = new byte[SNOW_KEY_LENGTH];
        int len = fnws.length();
        for (int i = 0; i < key.length; i++)
            key[i] = (byte) (i + (i % 3 + 2) * fnws.charAt(len - 1 - i % len));
        return key;
    }

    static long fnvHashSalt(String salt) {
        long keyHash = INITIAL_KEY_HASH;
        for (int i = 0; i < salt.length(); i++) {
            keyHash = (keyHash ^ salt.charAt(i)) * KEY_HASH_MULTIPLIER;
            keyHash &= 0xFFFFFFFFL;
        }
        return keyHash;
    }

    static byte[] deriveImgKey(Entry e, String salt, boolean chacha) {
        String keyHashStr = Long.toUnsignedString(fnvHashSalt(salt));
        byte[] digits = new byte[keyHashStr.length()];
        for (int i = 0; i < digits.length; i++) digits[i] = (byte) (keyHashStr.charAt(i) - '0');
        byte[] imgKey = new byte[chacha ? CHACHA_KEY_LENGTH : SNOW_KEY_LENGTH];
        String name = e.name;
        byte[] ek = e.entryKey;
        for (int i = 0; i < imgKey.length; i++) {
            int digitIdx = i % digits.length;
            int entryKeyIdx = (digits[(i + 2) % digits.length] + i) % ek.length;
            int mixed = (digits[digitIdx] % 2) + (ek[entryKeyIdx] & 0xFF) + ((digits[(i + 1) % digits.length] + i) % 5);
            imgKey[i] = (byte) (i + name.charAt(i % name.length()) * mixed);
            if (chacha) imgKey[i] ^= CHACHA20_KEY_OBSCURE[i];
        }
        return imgKey;
    }

    // ---------- ChaCha v4 ----------
    static MsFile loadChaCha(byte[] all, String fileName) {
        int randByteCount = charSum(fileName) % RAND_BYTE_MOD + RAND_BYTE_OFFSET;
        if (all.length < randByteCount + 5) throw new RuntimeException("too short");
        ByteBuffer bb = ByteBuffer.wrap(all).order(ByteOrder.LITTLE_ENDIAN);
        byte[] randBytes = new byte[randByteCount];
        bb.get(randBytes);
        for (int i = 0; i < randBytes.length; i++) randBytes[i] = (byte) (randBytes[i] >> 1);

        int version = (bb.get() & 0xFF) ^ (randBytes[0] & 0xFF);
        if (version != CHACHA_VERSION) throw new RuntimeException("version " + version);

        int hashedSaltLen = bb.getInt();
        int saltLen = (hashedSaltLen & 0xFF) ^ (randBytes[0] & 0xFF);
        if (saltLen <= 0 || bb.remaining() < saltLen * 2) throw new RuntimeException("bad saltLen " + saltLen);
        byte[] saltBytes = new byte[saltLen * 2];
        bb.get(saltBytes);
        char[] saltChars = new char[saltLen];
        for (int i = 0; i < saltLen; i++) {
            int a = (randBytes[i] & 0xFF) ^ (saltBytes[i * 2] & 0xFF);
            saltChars[i] = (char) (((a | 0x4B) << 1) - a - 75);
        }
        String salt = new String(saltChars);
        String fileNameWithSalt = fileName + salt;

        int headerStart = bb.position();
        byte[] headerKey = deriveChaChaHeaderKey(fileNameWithSalt);
        byte[] headerCipher = Arrays.copyOfRange(all, headerStart, headerStart + 64);
        new ChaCha20(headerKey, new byte[12], 0).transformBlock(headerCipher, 0, headerCipher, 0);
        ByteBuffer hb = ByteBuffer.wrap(headerCipher).order(ByteOrder.LITTLE_ENDIAN);
        hb.getInt(); // hash 不校验
        int entryCount = hb.getInt();

        int padAmount = tripleSum(fileName) % HEADER_PAD_MOD + CHACHA_HEADER_PAD_OFFSET;
        int entryStart = headerStart + CHACHA_HEADER_CIPHER_LEN + padAmount;

        byte[] entryTableKey = deriveChaChaEntryKey(fileNameWithSalt);
        MsFile f = new MsFile();
        f.all = all; f.fileNameLower = fileName; f.salt = salt; f.chacha = true;
        int physicalEnd = entryStart;
        ChaCha20Reader r = new ChaCha20Reader(all, entryStart, entryTableKey);
        for (int i = 0; i < entryCount; i++) {
            Entry e = new Entry();
            e.name = r.readString();
            e.checksum = r.readInt32(); e.flags = r.readInt32(); e.startPos = r.readInt32();
            e.size = r.readInt32(); e.sizeAligned = r.readInt32(); e.unk1 = r.readInt32(); e.unk2 = r.readInt32();
            e.entryKey = r.readBytes(ENTRY_KEY_SIZE);
            e.unk3 = r.readInt32(); e.unk4 = r.readInt32();
            f.entries.add(e);
        }
        physicalEnd = r.physicalPosition();
        r.close();
        f.dataStart = physicalEnd;
        if ((f.dataStart & PAGE_ALIGNMENT_MASK) != 0)
            f.dataStart = (f.dataStart & ~PAGE_ALIGNMENT_MASK) + 0x400;
        return f;
    }

    static byte[] deriveChaChaHeaderKey(String fnws) {
        byte[] key = new byte[CHACHA_KEY_LENGTH];
        int len = fnws.length();
        for (int i = 0; i < key.length; i++) {
            key[i] = (byte) (fnws.charAt(i % len) + i);
            key[i] ^= CHACHA20_KEY_OBSCURE[i];
        }
        return key;
    }

    static byte[] deriveChaChaEntryKey(String fnws) {
        byte[] key = new byte[CHACHA_KEY_LENGTH];
        int len = fnws.length();
        for (int i = 0; i < key.length; i++) {
            key[i] = (byte) (i + (i % 3 + 2) * fnws.charAt(len - 1 - i % len));
            key[i] ^= CHACHA20_KEY_OBSCURE[i];
        }
        return key;
    }

    // ---------- entry decrypt ----------
    static byte[] decryptEntry(MsFile f, Entry e) {
        int start = f.dataStart + e.startPos * BLOCK_ALIGNMENT;
        if (f.chacha) return decryptChaChaImage(f.all, start, e, f.salt);
        byte[] enc = Arrays.copyOfRange(f.all, start, start + e.sizeAligned);
        byte[] imgKey = deriveImgKey(e, f.salt, false);
        int size = e.size;
        byte[] firstPass = new Snow2(imgKey, false).transform(enc);
        byte[] result = new byte[size];
        int firstLen = Math.min(size, DOUBLE_ENCRYPT_INITIAL_BYTES);
        int secondInputLen = Math.min(firstPass.length, align4(firstLen));
        byte[] twice = new Snow2(imgKey, false).transform(Arrays.copyOf(firstPass, secondInputLen));
        System.arraycopy(twice, 0, result, 0, firstLen);
        if (size > firstLen) System.arraycopy(firstPass, firstLen, result, firstLen, size - firstLen);
        return result;
    }

    static byte[] decryptChaChaImage(byte[] all, int start, Entry e, String salt) {
        int size = e.size, sizeAligned = e.sizeAligned;
        byte[] enc = Arrays.copyOfRange(all, start, start + sizeAligned);
        byte[] imgKey = deriveImgKey(e, salt, true);
        byte[] nonce = new byte[12];
        int counter = deriveImgNonceAndCounter(salt, nonce);
        byte[] result = new byte[size];
        int cryptedSize = Math.min(size, DOUBLE_ENCRYPT_INITIAL_BYTES);
        if (cryptedSize > 0) {
            int processLen = (cryptedSize + 63) & ~63;
            byte[] chunk = Arrays.copyOf(enc, processLen);
            byte[] plain = new byte[processLen];
            new ChaCha20(imgKey, nonce, counter).transformBlock(chunk, 0, plain, 0);
            System.arraycopy(plain, 0, result, 0, cryptedSize);
        }
        if (size > DOUBLE_ENCRYPT_INITIAL_BYTES)
            System.arraycopy(enc, DOUBLE_ENCRYPT_INITIAL_BYTES, result, DOUBLE_ENCRYPT_INITIAL_BYTES, size - DOUBLE_ENCRYPT_INITIAL_BYTES);
        return result;
    }

    static int deriveImgNonceAndCounter(String salt, byte[] nonce) {
        int keyHash = (int) fnvHashSalt(salt);
        int keyHash2 = keyHash >>> 1;
        int keyHash3 = keyHash2 ^ 0x6C;
        byte[] keyHashData = new byte[12];
        ByteBuffer.wrap(keyHashData).order(ByteOrder.LITTLE_ENDIAN)
                .putInt(keyHash).putInt(keyHash2).putInt(keyHash3);
        int a = 0, b = 0, c = 90, d = 0;
        for (int i = 0; i < 12; i++) {
            keyHashData[i] ^= (byte) (d + 11 * (i / 11) + (c ^ (i >> 2)) + (a ^ b));
            d--; a += 8; b += 17; c += 43;
        }
        System.arraycopy(keyHashData, 0, nonce, 4, 8);
        return ByteBuffer.wrap(keyHashData, 8, 4).order(ByteOrder.LITTLE_ENDIAN).getInt();
    }

    // ---------- Snow2 ----------
    static final class Snow2 {
        private final boolean encrypting;
        private int s15, s14, s13, s12, s11, s10, s9, s8, s7, s6, s5, s4, s3, s2, s1, s0;
        private int r1, r2;
        private final int[] keyStream = new int[16];
        private int curIndex = 0;

        Snow2(byte[] key, boolean encrypting) {
            if (key.length != 16 && key.length != 32) throw new IllegalArgumentException("key size");
            this.encrypting = encrypting;
            loadKey(key);
            refreshKeyStream();
        }

        byte[] transform(byte[] input) {
            int totalBytes = input.length;
            int paddedLength = (totalBytes + 3) & ~3;
            byte[] temp = Arrays.copyOf(input, paddedLength);
            ByteBuffer bb = ByteBuffer.wrap(temp).order(ByteOrder.LITTLE_ENDIAN);
            for (int i = 0; i < paddedLength; i += 4) {
                int value = bb.getInt(i);
                int key = keyStream[curIndex];
                value = encrypting ? value + key : value - key;
                bb.putInt(i, value);
                if (++curIndex >= 16) { refreshKeyStream(); curIndex = 0; }
            }
            if (paddedLength == totalBytes) return temp;
            return Arrays.copyOf(temp, totalBytes);
        }

        private static int toInt32BE(byte[] k, int off) {
            return (k[off] << 24) | (k[off + 1] << 16) | (k[off + 2] << 8) | k[off + 3];
        }

        private void loadKey(byte[] key) {
            if (key.length == 16) {
                s15 = toInt32BE(key, 0); s14 = toInt32BE(key, 4); s13 = toInt32BE(key, 8); s12 = toInt32BE(key, 12);
                s11 = ~s15; s10 = ~s14; s9 = ~s13; s8 = ~s12;
                s7 = s15; s6 = s14; s5 = s13; s4 = s12;
                s3 = ~s15; s2 = ~s14; s1 = ~s13; s0 = ~s12;
            } else {
                s15 = toInt32BE(key, 0); s14 = toInt32BE(key, 4); s13 = toInt32BE(key, 8); s12 = toInt32BE(key, 12);
                s11 = toInt32BE(key, 16); s10 = toInt32BE(key, 20); s9 = toInt32BE(key, 24); s8 = toInt32BE(key, 28);
                s7 = ~s15; s6 = ~s14; s5 = ~s13; s4 = ~s12;
                s3 = ~s11; s2 = ~s10; s1 = ~s9; s0 = ~s8;
            }
            r1 = 0; r2 = 0;
            for (int i = 0; i < 2; i++) {
                int outfromFsm = (r1 + s15) ^ r2;
                s0 = aMul(s0) ^ s2 ^ aInvMul(s11) ^ outfromFsm;
                int fsmtmp = r2 + s5;
                r2 = T0(byteN(0, r1)) ^ T1(byteN(1, r1)) ^ T2(byteN(2, r1)) ^ T3(byteN(3, r1));
                r1 = fsmtmp;

                outfromFsm = (r1 + s0) ^ r2;
                s1 = aMul(s1) ^ s3 ^ aInvMul(s12) ^ outfromFsm;
                fsmtmp = r2 + s6;
                r2 = T0(byteN(0, r1)) ^ T1(byteN(1, r1)) ^ T2(byteN(2, r1)) ^ T3(byteN(3, r1));
                r1 = fsmtmp;

                outfromFsm = (r1 + s1) ^ r2;
                s2 = aMul(s2) ^ s4 ^ aInvMul(s13) ^ outfromFsm;
                fsmtmp = r2 + s7;
                r2 = T0(byteN(0, r1)) ^ T1(byteN(1, r1)) ^ T2(byteN(2, r1)) ^ T3(byteN(3, r1));
                r1 = fsmtmp;

                outfromFsm = (r1 + s2) ^ r2;
                s3 = aMul(s3) ^ s5 ^ aInvMul(s14) ^ outfromFsm;
                fsmtmp = r2 + s8;
                r2 = T0(byteN(0, r1)) ^ T1(byteN(1, r1)) ^ T2(byteN(2, r1)) ^ T3(byteN(3, r1));
                r1 = fsmtmp;

                outfromFsm = (r1 + s3) ^ r2;
                s4 = aMul(s4) ^ s6 ^ aInvMul(s15) ^ outfromFsm;
                fsmtmp = r2 + s9;
                r2 = T0(byteN(0, r1)) ^ T1(byteN(1, r1)) ^ T2(byteN(2, r1)) ^ T3(byteN(3, r1));
                r1 = fsmtmp;

                outfromFsm = (r1 + s4) ^ r2;
                s5 = aMul(s5) ^ s7 ^ aInvMul(s0) ^ outfromFsm;
                fsmtmp = r2 + s10;
                r2 = T0(byteN(0, r1)) ^ T1(byteN(1, r1)) ^ T2(byteN(2, r1)) ^ T3(byteN(3, r1));
                r1 = fsmtmp;

                outfromFsm = (r1 + s5) ^ r2;
                s6 = aMul(s6) ^ s8 ^ aInvMul(s1) ^ outfromFsm;
                fsmtmp = r2 + s11;
                r2 = T0(byteN(0, r1)) ^ T1(byteN(1, r1)) ^ T2(byteN(2, r1)) ^ T3(byteN(3, r1));
                r1 = fsmtmp;

                outfromFsm = (r1 + s6) ^ r2;
                s7 = aMul(s7) ^ s9 ^ aInvMul(s2) ^ outfromFsm;
                fsmtmp = r2 + s12;
                r2 = T0(byteN(0, r1)) ^ T1(byteN(1, r1)) ^ T2(byteN(2, r1)) ^ T3(byteN(3, r1));
                r1 = fsmtmp;

                outfromFsm = (r1 + s7) ^ r2;
                s8 = aMul(s8) ^ s10 ^ aInvMul(s3) ^ outfromFsm;
                fsmtmp = r2 + s13;
                r2 = T0(byteN(0, r1)) ^ T1(byteN(1, r1)) ^ T2(byteN(2, r1)) ^ T3(byteN(3, r1));
                r1 = fsmtmp;

                outfromFsm = (r1 + s8) ^ r2;
                s9 = aMul(s9) ^ s11 ^ aInvMul(s4) ^ outfromFsm;
                fsmtmp = r2 + s14;
                r2 = T0(byteN(0, r1)) ^ T1(byteN(1, r1)) ^ T2(byteN(2, r1)) ^ T3(byteN(3, r1));
                r1 = fsmtmp;

                outfromFsm = (r1 + s9) ^ r2;
                s10 = aMul(s10) ^ s12 ^ aInvMul(s5) ^ outfromFsm;
                fsmtmp = r2 + s15;
                r2 = T0(byteN(0, r1)) ^ T1(byteN(1, r1)) ^ T2(byteN(2, r1)) ^ T3(byteN(3, r1));
                r1 = fsmtmp;

                outfromFsm = (r1 + s10) ^ r2;
                s11 = aMul(s11) ^ s13 ^ aInvMul(s6) ^ outfromFsm;
                fsmtmp = r2 + s0;
                r2 = T0(byteN(0, r1)) ^ T1(byteN(1, r1)) ^ T2(byteN(2, r1)) ^ T3(byteN(3, r1));
                r1 = fsmtmp;

                outfromFsm = (r1 + s11) ^ r2;
                s12 = aMul(s12) ^ s14 ^ aInvMul(s7) ^ outfromFsm;
                fsmtmp = r2 + s1;
                r2 = T0(byteN(0, r1)) ^ T1(byteN(1, r1)) ^ T2(byteN(2, r1)) ^ T3(byteN(3, r1));
                r1 = fsmtmp;

                outfromFsm = (r1 + s12) ^ r2;
                s13 = aMul(s13) ^ s15 ^ aInvMul(s8) ^ outfromFsm;
                fsmtmp = r2 + s2;
                r2 = T0(byteN(0, r1)) ^ T1(byteN(1, r1)) ^ T2(byteN(2, r1)) ^ T3(byteN(3, r1));
                r1 = fsmtmp;

                outfromFsm = (r1 + s13) ^ r2;
                s14 = aMul(s14) ^ s0 ^ aInvMul(s9) ^ outfromFsm;
                fsmtmp = r2 + s3;
                r2 = T0(byteN(0, r1)) ^ T1(byteN(1, r1)) ^ T2(byteN(2, r1)) ^ T3(byteN(3, r1));
                r1 = fsmtmp;

                outfromFsm = (r1 + s14) ^ r2;
                s15 = aMul(s15) ^ s1 ^ aInvMul(s10) ^ outfromFsm;
                fsmtmp = r2 + s4;
                r2 = T0(byteN(0, r1)) ^ T1(byteN(1, r1)) ^ T2(byteN(2, r1)) ^ T3(byteN(3, r1));
                r1 = fsmtmp;
            }
        }

        private void refreshKeyStream() {
            int fsmtmp;
            s0 = aMul(s0) ^ s2 ^ aInvMul(s11);
            fsmtmp = r2 + s5;
            r2 = T0(byteN(0, r1)) ^ T1(byteN(1, r1)) ^ T2(byteN(2, r1)) ^ T3(byteN(3, r1));
            r1 = fsmtmp;
            keyStream[0] = (r1 + s0) ^ r2 ^ s1;
            s1 = aMul(s1) ^ s3 ^ aInvMul(s12);
            fsmtmp = r2 + s6;
            r2 = T0(byteN(0, r1)) ^ T1(byteN(1, r1)) ^ T2(byteN(2, r1)) ^ T3(byteN(3, r1));
            r1 = fsmtmp;
            keyStream[1] = (r1 + s1) ^ r2 ^ s2;
            s2 = aMul(s2) ^ s4 ^ aInvMul(s13);
            fsmtmp = r2 + s7;
            r2 = T0(byteN(0, r1)) ^ T1(byteN(1, r1)) ^ T2(byteN(2, r1)) ^ T3(byteN(3, r1));
            r1 = fsmtmp;
            keyStream[2] = (r1 + s2) ^ r2 ^ s3;
            s3 = aMul(s3) ^ s5 ^ aInvMul(s14);
            fsmtmp = r2 + s8;
            r2 = T0(byteN(0, r1)) ^ T1(byteN(1, r1)) ^ T2(byteN(2, r1)) ^ T3(byteN(3, r1));
            r1 = fsmtmp;
            keyStream[3] = (r1 + s3) ^ r2 ^ s4;
            s4 = aMul(s4) ^ s6 ^ aInvMul(s15);
            fsmtmp = r2 + s9;
            r2 = T0(byteN(0, r1)) ^ T1(byteN(1, r1)) ^ T2(byteN(2, r1)) ^ T3(byteN(3, r1));
            r1 = fsmtmp;
            keyStream[4] = (r1 + s4) ^ r2 ^ s5;
            s5 = aMul(s5) ^ s7 ^ aInvMul(s0);
            fsmtmp = r2 + s10;
            r2 = T0(byteN(0, r1)) ^ T1(byteN(1, r1)) ^ T2(byteN(2, r1)) ^ T3(byteN(3, r1));
            r1 = fsmtmp;
            keyStream[5] = (r1 + s5) ^ r2 ^ s6;
            s6 = aMul(s6) ^ s8 ^ aInvMul(s1);
            fsmtmp = r2 + s11;
            r2 = T0(byteN(0, r1)) ^ T1(byteN(1, r1)) ^ T2(byteN(2, r1)) ^ T3(byteN(3, r1));
            r1 = fsmtmp;
            keyStream[6] = (r1 + s6) ^ r2 ^ s7;
            s7 = aMul(s7) ^ s9 ^ aInvMul(s2);
            fsmtmp = r2 + s12;
            r2 = T0(byteN(0, r1)) ^ T1(byteN(1, r1)) ^ T2(byteN(2, r1)) ^ T3(byteN(3, r1));
            r1 = fsmtmp;
            keyStream[7] = (r1 + s7) ^ r2 ^ s8;
            s8 = aMul(s8) ^ s10 ^ aInvMul(s3);
            fsmtmp = r2 + s13;
            r2 = T0(byteN(0, r1)) ^ T1(byteN(1, r1)) ^ T2(byteN(2, r1)) ^ T3(byteN(3, r1));
            r1 = fsmtmp;
            keyStream[8] = (r1 + s8) ^ r2 ^ s9;
            s9 = aMul(s9) ^ s11 ^ aInvMul(s4);
            fsmtmp = r2 + s14;
            r2 = T0(byteN(0, r1)) ^ T1(byteN(1, r1)) ^ T2(byteN(2, r1)) ^ T3(byteN(3, r1));
            r1 = fsmtmp;
            keyStream[9] = (r1 + s9) ^ r2 ^ s10;
            s10 = aMul(s10) ^ s12 ^ aInvMul(s5);
            fsmtmp = r2 + s15;
            r2 = T0(byteN(0, r1)) ^ T1(byteN(1, r1)) ^ T2(byteN(2, r1)) ^ T3(byteN(3, r1));
            r1 = fsmtmp;
            keyStream[10] = (r1 + s10) ^ r2 ^ s11;
            s11 = aMul(s11) ^ s13 ^ aInvMul(s6);
            fsmtmp = r2 + s0;
            r2 = T0(byteN(0, r1)) ^ T1(byteN(1, r1)) ^ T2(byteN(2, r1)) ^ T3(byteN(3, r1));
            r1 = fsmtmp;
            keyStream[11] = (r1 + s11) ^ r2 ^ s12;
            s12 = aMul(s12) ^ s14 ^ aInvMul(s7);
            fsmtmp = r2 + s1;
            r2 = T0(byteN(0, r1)) ^ T1(byteN(1, r1)) ^ T2(byteN(2, r1)) ^ T3(byteN(3, r1));
            r1 = fsmtmp;
            keyStream[12] = (r1 + s12) ^ r2 ^ s13;
            s13 = aMul(s13) ^ s15 ^ aInvMul(s8);
            fsmtmp = r2 + s2;
            r2 = T0(byteN(0, r1)) ^ T1(byteN(1, r1)) ^ T2(byteN(2, r1)) ^ T3(byteN(3, r1));
            r1 = fsmtmp;
            keyStream[13] = (r1 + s13) ^ r2 ^ s14;
            s14 = aMul(s14) ^ s0 ^ aInvMul(s9);
            fsmtmp = r2 + s3;
            r2 = T0(byteN(0, r1)) ^ T1(byteN(1, r1)) ^ T2(byteN(2, r1)) ^ T3(byteN(3, r1));
            r1 = fsmtmp;
            keyStream[14] = (r1 + s14) ^ r2 ^ s15;
            s15 = aMul(s15) ^ s1 ^ aInvMul(s10);
            fsmtmp = r2 + s4;
            r2 = T0(byteN(0, r1)) ^ T1(byteN(1, r1)) ^ T2(byteN(2, r1)) ^ T3(byteN(3, r1));
            r1 = fsmtmp;
            keyStream[15] = (r1 + s15) ^ r2 ^ s0;
        }

        private static int byteN(int n, int w) { return (w >>> (n * 8)) & 0xFF; }
        private static int aInvMul(int w) { return (w >>> 8) ^ SnowTables.SNOW_ALPHAINV_MUL[w & 0xFF]; }
        private static int aMul(int w) { return (w << 8) ^ SnowTables.SNOW_ALPHA_MUL[(w >>> 24) & 0xFF]; }
        private static int T0(int b) { return SnowTables.SNOW_T0[b]; }
        private static int T1(int b) { return SnowTables.SNOW_T1[b]; }
        private static int T2(int b) { return SnowTables.SNOW_T2[b]; }
        private static int T3(int b) { return SnowTables.SNOW_T3[b]; }
    }

    // ---------- ChaCha20 (RFC 7539, 与 WzComparerR2 移植一致) ----------
    static final class ChaCha20 {
        private final int[] state = new int[16];

        ChaCha20(byte[] key, byte[] nonce, int counter) {
            byte[] constants = "expand 32-byte k".getBytes(StandardCharsets.US_ASCII);
            int keyIndex = key.length - 16;
            state[4] = u8to32(key, 0); state[5] = u8to32(key, 4); state[6] = u8to32(key, 8); state[7] = u8to32(key, 12);
            state[8] = u8to32(key, keyIndex); state[9] = u8to32(key, keyIndex + 4);
            state[10] = u8to32(key, keyIndex + 8); state[11] = u8to32(key, keyIndex + 12);
            state[0] = u8to32(constants, 0); state[1] = u8to32(constants, 4);
            state[2] = u8to32(constants, 8); state[3] = u8to32(constants, 12);
            state[12] = counter;
            state[13] = u8to32(nonce, 0); state[14] = u8to32(nonce, 4); state[15] = u8to32(nonce, 8);
        }

        void transformBlock(byte[] input, int inOff, byte[] output, int outOff) {
            int len = input.length - inOff;
            if (len <= 0 || (len & 63) != 0) throw new IllegalArgumentException("len%64");
            for (int written = 0; written < len; written += 64) block64(input, inOff + written, output, outOff + written);
        }

        /** 与原实现一致：counter 归零（keystream 每 64 字节重置的怪癖） */
        void resetCounter() { state[12] = 0; }

        private void block64(byte[] input, int inOff, byte[] output, int outOff) {
            int[] x = Arrays.copyOf(state, 16);
            for (int round = 0; round < 10; round++) {
                qr(x, 0, 4, 8, 12); qr(x, 1, 5, 9, 13); qr(x, 2, 6, 10, 14); qr(x, 3, 7, 11, 15);
                qr(x, 0, 5, 10, 15); qr(x, 1, 6, 11, 12); qr(x, 2, 7, 8, 13); qr(x, 3, 4, 9, 14);
            }
            byte[] tmp = new byte[64];
            for (int i = 0; i < 16; i++) {
                int v = x[i] + state[i];
                tmp[i * 4] = (byte) v; tmp[i * 4 + 1] = (byte) (v >>> 8);
                tmp[i * 4 + 2] = (byte) (v >>> 16); tmp[i * 4 + 3] = (byte) (v >>> 24);
            }
            state[12] += 1;
            if (state[12] == 0) state[13] += 1;
            for (int i = 0; i < 64; i++) output[outOff + i] = (byte) ((input[inOff + i] & 0xFF) ^ (tmp[i] & 0xFF));
        }

        private static void qr(int[] x, int a, int b, int c, int d) {
            x[a] += x[b]; x[d] = Integer.rotateLeft(x[d] ^ x[a], 16);
            x[c] += x[d]; x[b] = Integer.rotateLeft(x[b] ^ x[c], 12);
            x[a] += x[b]; x[d] = Integer.rotateLeft(x[d] ^ x[a], 8);
            x[c] += x[d]; x[b] = Integer.rotateLeft(x[b] ^ x[c], 7);
        }

        private static int u8to32(byte[] p, int off) {
            return (p[off] & 0xFF) | ((p[off + 1] & 0xFF) << 8) | ((p[off + 2] & 0xFF) << 16) | ((p[off + 3] & 0xFF) << 24);
        }
    }

    // ---------- ChaCha20Reader（保留 64 字节块 counter 归零怪癖） ----------
    static final class ChaCha20Reader {
        private final byte[] data;
        private int dataPos;
        private final ChaCha20 cipher;
        private final byte[] buffer = new byte[64];
        private int bufferReadPos = 64;

        ChaCha20Reader(byte[] data, int startPos, byte[] key) {
            this.data = data;
            this.dataPos = startPos;
            this.cipher = new ChaCha20(key, new byte[12], 0);
        }

        int physicalPosition() { return dataPos; }

        int readInt32() {
            byte[] t = readBytes(4);
            return ByteBuffer.wrap(t).order(ByteOrder.LITTLE_ENDIAN).getInt();
        }

        String readString() {
            int strLen = readInt32();
            byte[] raw = readBytes(strLen * 2);
            char[] chars = new char[strLen];
            for (int i = 0; i < strLen; i++)
                chars[i] = (char) ((raw[i * 2] & 0xFF) | ((raw[i * 2 + 1] & 0xFF) << 8));
            return new String(chars);
        }

        byte[] readBytes(int count) {
            byte[] out = new byte[count];
            int written = 0;
            while (written < count) {
                if (bufferReadPos >= buffer.length) {
                    System.arraycopy(data, dataPos, buffer, 0, buffer.length);
                    cipher.transformBlock(buffer, 0, buffer, 0);
                    dataPos += buffer.length;
                    bufferReadPos = 0;
                }
                int copy = Math.min(count - written, buffer.length - bufferReadPos);
                System.arraycopy(buffer, bufferReadPos, out, written, copy);
                bufferReadPos += copy;
                written += copy;
            }
            if (bufferReadPos >= buffer.length) {
                // 与原实现一致：counter 归零（keystream 每 64 字节重置）
                // cipher.state[12] = 0;  -- 需要访问 state，直接重置
                cipher.resetCounter();
                bufferReadPos = buffer.length;
            }
            return out;
        }

        void close() { }
    }
}
