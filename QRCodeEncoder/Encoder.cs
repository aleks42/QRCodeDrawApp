using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

// Алгоритм генерации QR-кода
// https://habr.com/ru/post/172525/

namespace QRCodeEncoder
{
    public class Encoder
    {
        /// <summary>
        /// Способ кодирования
        /// </summary>
        private enum EncodingType
        {
            /// <summary>
            /// Цифровое кодирование
            /// </summary>
            Numeric,

            /// <summary>
            /// Буквенно-цифровое кодирование
            /// </summary>
            Alphanumeric,

            /// <summary>
            /// Побайтовое кодирование
            /// </summary>
            Byte
        }

        public EncoderRes Encode(string str, CorrectionLevel cLevel)
        {
            // Кодирование данных

            var encodingType = GetEncodingType(str);
            var ver = 0;
            string dataLen;
            string encodingTypeBits;
            string strBits;

            switch (encodingType)
            {
                case EncodingType.Numeric:
                    strBits = EncodeNumeric(str);
                    encodingTypeBits = "0001";
                    break;
                case EncodingType.Alphanumeric:
                    strBits = EncodeAlfanumeric(str);
                    encodingTypeBits = "0010";
                    break;
                case EncodingType.Byte:
                default:
                    strBits = EncodeByte(str);
                    encodingTypeBits = "0100";
                    break;
            }

            // Добавление служебной информации

            var strBitsCopy = strBits;

            while (GetQRVersion(strBitsCopy.Length, cLevel) > ver)
            {
                ver = GetQRVersion(strBitsCopy.Length, cLevel);
                dataLen = GetDataAmount(ver, str.Length, encodingType);
                strBitsCopy = encodingTypeBits + dataLen + strBits;
            }

            // дополняем последовательность до целых байт

            strBits = strBitsCopy.PadRight(
                (int)Math.Ceiling((decimal)strBitsCopy.Length / 8) * 8, '0');

            // дополняем последовательность до длины выбранной версии qr кода

            var versionList = Version[cLevel];

            var b1 = "11101100";
            var b2 = "00010001";

            while (versionList[ver - 1] > strBits.Length)
            {
                strBits += b1;
                if (versionList[ver - 1] > strBits.Length) strBits += b2;
            }

            var dataByteStrings =
                Enumerable.Range(0, strBits.Length / 8)
                          .Select(i => strBits.Substring(8 * i, 8));
            var dataBytes = dataByteStrings.Select(x => Convert.ToByte(x, 2)).ToArray();
                       
            // разделение последовательности на блоки

            var blocksCount = GetBlocks(ver, cLevel);
            var blockLength = dataBytes.Length / blocksCount;
            var longBlocksCount = dataBytes.Length % blocksCount;

            var blocks = new byte[blocksCount][];
            var blockLen = new List<int>();

            for (int i = 0; i < blocksCount; i++)
                blockLen.Add(blockLength);

            for (int i = blocksCount - 1; i >= 0; i--)
            {
                if (blocksCount - i > longBlocksCount) break;
                blockLen[i]++;
            }

            for (int i = 0; i < blocksCount; i++)
            {
                blocks[i] = dataBytes
                    .Skip(blockLen.Take(i).Sum())
                    .Take(blockLen[i])
                    .ToArray();
            }

            // создание байтов коррекции

            var correctionBytesCount = GetCorrectionBytesCount(ver, cLevel);
            var polynomial = Polynomials[correctionBytesCount];
            byte[][] correctionBlocks = new byte[blocks.Length][];

            for (int i1 = 0; i1 < blocks.Length; i1++)
            {
                correctionBlocks[i1] = GetCorrectionBytes(blocks[i1], polynomial);

                for (int i2 = 1; i2 < blocks[i1].Length; i2++)
                {
                    correctionBlocks[i1] = GetCorrectionBytes(correctionBlocks[i1], polynomial);
                }
            }

            // Объединение блоков

            var blocksJoin = new byte[blocks.Select(x => x.Length).Sum() + correctionBlocks.Select(x => x.Length).Sum()];
            var blocksJoinCounter = 0;

            for (int i2 = 0; i2 < blocks[blocks.Length - 1].Length; i2++)
            {
                for (int i1 = 0; i1 < blocks.Length; i1++)
                {
                    if(i2 < blocks[i1].Length)
                    {
                        blocksJoin[blocksJoinCounter++] = blocks[i1][i2];
                    }
                }
            }

            for (int i2 = 0; i2 < correctionBlocks[correctionBlocks.Length - 1].Length; i2++)
            {
                for (int i1 = 0; i1 < correctionBlocks.Length; i1++)
                {
                    if (i2 < correctionBlocks[i1].Length)
                    {
                        blocksJoin[blocksJoinCounter++] = correctionBlocks[i1][i2];
                    }
                }
            }

            return new EncoderRes { Data = blocksJoin, Version = ver };
        }

        //

        /// <summary>
        /// Алгоритм получения байтов коррекции
        /// </summary>
        private static byte[] GetCorrectionBytes(byte[] src, byte[] polynomial)
        {
            var correctionArray = new byte[Math.Max(src.Length, polynomial.Length)];

            Array.Copy(src, 1, correctionArray, 0, src.Length - 1);
            var a = src[0];
            if (a != 0)
            {
                var aGaluaReverse = GaluaFieldReverse[a];

                var polynomialCopy = new byte[polynomial.Length];
                Array.Copy(polynomial, polynomialCopy, polynomial.Length);

                for (int i = 0; i < polynomialCopy.Length; i++)
                {
                    var sum = polynomialCopy[i] + aGaluaReverse;
                    if (sum > 254) sum %= 255;
                    polynomialCopy[i] = (byte)sum;
                }

                for (int i = 0; i < polynomialCopy.Length; i++)
                {
                    polynomialCopy[i] = GaluaField[polynomialCopy[i]];
                }

                for (int i = 0; i < polynomialCopy.Length; i++)
                {
                    correctionArray[i] = (byte)(correctionArray[i] ^ polynomialCopy[i]);
                }
            }
            return correctionArray;
        }

        /// <summary>
        /// Определение способа кодирования
        /// </summary>
        private EncodingType GetEncodingType(string str)
        {
            if (str.All(x => char.IsDigit(x)))
            {
                return EncodingType.Numeric;
            }
            else if (str.All(x => AlfanumericCodes.ContainsKey(x)))
            {
                return EncodingType.Alphanumeric;
            }
            else
            {
                return EncodingType.Byte;
            }
        }

        /// <summary>
        /// Цифровое кодирование
        /// </summary>
        private string EncodeNumeric(string str)
        {
            var res = new StringBuilder();

            for (int i = 0; i < str.Length; i += 3)
            {
                if (i + 2 < str.Length)
                {
                    var x1 = $"{str[i]}{str[i + 1]}{str[i + 2]}";
                    res.Append(Convert.ToString(Convert.ToInt32(x1), 2).PadLeft(10, '0'));
                }
                else if (i + 1 < str.Length)
                {
                    var x1 = $"{str[i]}{str[i + 1]}";
                    res.Append(Convert.ToString(Convert.ToInt32(x1), 2).PadLeft(7, '0'));
                }
                else
                {
                    var x1 = $"{str[i]}";
                    res.Append(Convert.ToString(Convert.ToInt32(x1), 2).PadLeft(6, '0'));
                }
            }

            return res.ToString();
        }

        /// <summary>
        /// Буквенно-цифровое кодирование
        /// </summary>
        private string EncodeAlfanumeric(string str)
        {
            var res = new StringBuilder();

            for (int i = 0; i < str.Length; i += 2)
            {
                if (i + 1 < str.Length)
                {
                    var x1 = AlfanumericCodes[str[i]];
                    var x2 = AlfanumericCodes[str[i + 1]];
                    res.Append(Convert.ToString(x1 * 45 + x2, 2).PadLeft(11, '0'));
                }
                else
                {
                    var x1 = AlfanumericCodes[str[i]];
                    res.Append(Convert.ToString(x1, 2).PadLeft(6, '0'));
                }
            }

            return res.ToString();
        }

        /// <summary>
        /// Побайтовое кодирование
        /// </summary>
        private string EncodeByte(string str)
        {
            var bytes = Encoding.UTF8.GetBytes(str);
            return string.Join("", bytes.Select(x => Convert.ToString(x, 2).PadLeft(8, '0')).ToArray());
        }

        /// <summary>
        /// Выбор версии
        /// </summary>
        private int GetQRVersion(int length, CorrectionLevel cLevel)
        {
            var versionList = Version[cLevel];

            for (int i = 0; i < versionList.Length; i++)
            {
                if (length < versionList[i]) return i + 1;
            }

            throw new ArgumentException(nameof(length));
        }

        /// <summary>
        /// Длина поля количества данных
        /// </summary>
        private string GetDataAmount(int ver, int length, EncodingType encodingType)
        {
            int dataLen;
            switch (encodingType)
            {
                case EncodingType.Numeric:
                    if (ver <= 9) dataLen = 10;
                    else if (ver <= 26) dataLen = 12;
                    else dataLen = 14;

                    return Convert.ToString(length, 2).PadLeft(dataLen, '0');
                case EncodingType.Alphanumeric:
                    if (ver <= 9) dataLen = 9;
                    else if (ver <= 26) dataLen = 11;
                    else dataLen = 13;

                    return Convert.ToString(length, 2).PadLeft(dataLen, '0');
                case EncodingType.Byte:
                default:
                    if (ver <= 9) dataLen = 8;
                    else if (ver <= 26) dataLen = 16;
                    else dataLen = 16;

                    return Convert.ToString(length, 2).PadLeft(dataLen, '0');
            }
        }

        /// <summary>
        /// Количество блоков
        /// </summary>
        private int GetBlocks(int ver, CorrectionLevel cLevel) => Blocks[cLevel][ver - 1];

        /// <summary>
        /// Количество байтов коррекции на один блок
        /// </summary>
        private int GetCorrectionBytesCount(int ver, CorrectionLevel cLevel) => CorrectionBytesCount[cLevel][ver - 1];

        #region Data

        /// <summary>
        /// Поле Галуа
        /// </summary>
        private static readonly byte[] GaluaField = new byte[]
        {
            1,2,4,8,16,32,64,128,29,58,116,232,205,135,19,38,
            76,152,45,90,180,117,234,201,143,3,6,12,24,48,96,192,
            157,39,78,156,37,74,148,53,106,212,181,119,238,193,159,35,
            70,140,5,10,20,40,80,160,93,186,105,210,185,111,222,161,
            95,190,97,194,153,47,94,188,101,202,137,15,30,60,120,240,
            253,231,211,187,107,214,177,127,254,225,223,163,91,182,113,226,
            217,175,67,134,17,34,68,136,13,26,52,104,208,189,103,206,
            129,31,62,124,248,237,199,147,59,118,236,197,151,51,102,204,
            133,23,46,92,184,109,218,169,79,158,33,66,132,21,42,84,
            168,77,154,41,82,164,85,170,73,146,57,114,228,213,183,115,
            230,209,191,99,198,145,63,126,252,229,215,179,123,246,241,255,
            227,219,171,75,150,49,98,196,149,55,110,220,165,87,174,65,
            130,25,50,100,200,141,7,14,28,56,112,224,221,167,83,166,
            81,162,89,178,121,242,249,239,195,155,43,86,172,69,138,9,
            18,36,72,144,61,122,244,245,247,243,251,235,203,139,11,22,
            44,88,176,125,250,233,207,131,27,54,108,216,173,71,142,1
        };

        /// <summary>
        /// Обратное поле Галуа
        /// </summary>
        private static readonly byte[] GaluaFieldReverse = new byte[]
        {
            0,0,1,25,2,50,26,198,3,223,51,238,27,104,199,75,
            4,100,224,14,52,141,239,129,28,193,105,248,200,8,76,113,
            5,138,101,47,225,36,15,33,53,147,142,218,240,18,130,69,
            29,181,194,125,106,39,249,185,201,154,9,120,77,228,114,166,
            6,191,139,98,102,221,48,253,226,152,37,179,16,145,34,136,
            54,208,148,206,143,150,219,189,241,210,19,92,131,56,70,64,
            30,66,182,163,195,72,126,110,107,58,40,84,250,133,186,61,
            202,94,155,159,10,21,121,43,78,212,229,172,115,243,167,87,
            7,112,192,247,140,128,99,13,103,74,222,237,49,197,254,24,
            227,165,153,119,38,184,180,124,17,68,146,217,35,32,137,46,
            55,63,209,91,149,188,207,205,144,135,151,178,220,252,190,97,
            242,86,211,171,20,42,93,158,132,60,57,83,71,109,65,162,
            31,45,67,216,183,123,164,118,196,23,73,236,127,12,111,246,
            108,161,59,82,41,157,85,170,251,96,134,177,187,204,62,90,
            203,89,95,176,156,169,160,81,11,245,22,235,122,117,44,215,
            79,174,213,233,230,231,173,232,116,214,244,234,168,80,88,175
        };

        /// <summary>
        /// Генирирующие многочлены
        /// </summary>
        private static readonly Dictionary<int, byte[]> Polynomials = new Dictionary<int, byte[]>
        {
            { 7,  new byte[] {87,229,146,149,238,102,21}},
            { 10, new byte[] {251,67,46,61,118,70,64,94,32,45}},
            { 13, new byte[] {74,152,176,100,86,100,106,104,130,218,206,140,78}},
            { 15, new byte[] {8,183,61,91,202,37,51,58,58,237,140,124,5,99,105}},
            { 16, new byte[] {120,104,107,109,102,161,76,3,91,191,147,169,182,194,225,120}},
            { 17, new byte[] {43,139,206,78,43,239,123,206,214,147,24,99,150,39,243,163,136}},
            { 18, new byte[] {215,234,158,94,184,97,118,170,79,187,152,148,252,179,5,98,96,153}},
            { 20, new byte[] {17,60,79,50,61,163,26,187,202,180,221,225,83,239,156,164,212,212,188,190}},
            { 22, new byte[] {210,171,247,242,93,230,14,109,221,53,200,74,8,172,98,80,219,134,160,105,165,231}},
            { 24, new byte[] {229,121,135,48,211,117,251,126,159,180,169,152,192,226,228,218,111,0,117,232,87,96,227,21}},
            { 26, new byte[] {173,125,158,2,103,182,118,17,145,201,111,28,165,53,161,21,245,142,13,102,48,227,153,145,218,70}},
            { 28, new byte[] {168,223,200,104,224,234,108,180,110,190,195,147,205,27,232,201,21,43,245,87,42,195,212,119,242,37,9,123}},
            { 30, new byte[] {41,173,145,152,216,31,179,182,50,48,110,86,239,96,222,125,42,173,226,193,224,130,156,37,251,216,238,40,192,180}},
        };

        /// <summary>
        /// Количество байтов коррекции на один блок
        /// </summary>
        private static readonly Dictionary<CorrectionLevel, int[]> CorrectionBytesCount = new Dictionary<CorrectionLevel, int[]>
        {
            { CorrectionLevel.L, new [] { 7,10,15,20,26,18,20,24,30,18,20,24,26,30,22,24,28,30,28,28,28,28,30,30,26,28,30,30,30,30,30,30,30,30,30,30,30,30,30,30 } },
            { CorrectionLevel.M, new [] { 10,16,26,18,24,16,18,22,22,26,30,22,22,24,24,28,28,26,26,26,26,28,28,28,28,28,28,28,28,28,28,28,28,28,28,28,28,28,28,28 } },
            { CorrectionLevel.Q, new [] { 13,22,18,26,18,24,18,22,20,24,28,26,24,20,30,24,28,28,26,30,28,30,30,30,30,28,30,30,30,30,30,30,30,30,30,30,30,30,30,30 } },
            { CorrectionLevel.H, new [] { 17,28,22,16,22,28,26,26,24,28,24,28,22,24,24,30,28,28,26,28,30,24,30,30,30,30,30,30,30,30,30,30,30,30,30,30,30,30,30,30 } }
        };

        /// <summary>
        /// Количество блоков по номеру версии
        /// </summary>
        private static readonly Dictionary<CorrectionLevel, int[]> Blocks = new Dictionary<CorrectionLevel, int[]>
        {
            { CorrectionLevel.L, new [] { 1,1,1,1,1,2,2,2,2,4,4,4,4,4,6,6,6,6,7,8,8,9,9,10,12,12,12,13,14,15,16,17,18,19,19,20,21,22,24,25 } },
            { CorrectionLevel.M, new [] { 1,1,1,2,2,4,4,4,5,5,5,8,9,9,10,10,11,13,14,16,17,17,18,20,21,23,25,26,28,29,31,33,35,37,38,40,43,45,47,49 } },
            { CorrectionLevel.Q, new [] { 1,1,2,2,4,4,6,6,8,8,8,10,12,16,12,17,16,18,21,20,23,23,25,27,29,34,34,35,38,40,43,45,48,51,53,56,59,62,65,68 } },
            { CorrectionLevel.H, new [] { 1,1,2,4,4,4,5,6,8,8,11,11,16,16,18,16,19,21,25,25,25,34,30,32,35,37,40,42,45,48,51,54,57,60,63,66,70,74,77,81 } }
        };

        /// <summary>
        /// Максимальное количество информации по номеру версии
        /// </summary>
        private static readonly Dictionary<CorrectionLevel, int[]> Version = new Dictionary<CorrectionLevel, int[]>
        {
            { CorrectionLevel.L, new [] { 152,272,440,640,864,1088,1248,1552,1856,2192,
                                          2592,2960,3424,3688,4184,4712,5176,5768,6360,6888,
                                          7456,8048,8752,9392,10208,10960,11744,12248,13048,13880,
                                          14744,15640,16568,17528,18448,19472,20528,21616,22496,23648 } },
            { CorrectionLevel.M, new [] { 128,224,352,512,688,864,992,1232,1456,1728,
                                          2032,2320,2672,2920,3320,3624,4056,4504,5016,5352,
                                          5712,6256,6880,7312,8000,8496,9024,9544,10136,10984,
                                          11640,12328,13048,13800,14496,15312,15936,16816,17728,18672 } },
            { CorrectionLevel.Q, new [] { 104,176,272,384,496,608,704,880,1056,1232,
                                          1440,1648,1952,2088,2360,2600,2936,3176,3560,3880,
                                          4096,4544,4912,5312,5744,6032,6464,6968,7288,7880,
                                          8264,8920,9368,9848,10288,10832,11408,12016,12656,13328 } },
            { CorrectionLevel.H, new [] { 72,128,208,288,368,480,528,688,800,976,
                                          1120,1264,1440,1576,1784,2024,2264,2504,2728,3080,
                                          3248,3536,3712,4112,4304,4768,5024,5288,5608,5960,
                                          6344,6760,7208,7688,7888,8432,8768,9136,9776,10208 } },
        };

        /// <summary>
        /// Значения символов в буквенно-цифровом кодировании
        /// </summary>
        private static readonly Dictionary<char, int> AlfanumericCodes = new Dictionary<char, int>
        {
            { '0', 0 },
            { '1', 1 },
            { '2', 2 },
            { '3', 3 },
            { '4', 4 },
            { '5', 5 },
            { '6', 6 },
            { '7', 7 },
            { '8', 8 },
            { '9', 9 },

            { 'A', 10 },
            { 'B', 11 },
            { 'C', 12 },
            { 'D', 13 },
            { 'E', 14 },
            { 'F', 15 },
            { 'G', 16 },
            { 'H', 17 },
            { 'I', 18 },
            { 'J', 19 },
            { 'K', 20 },
            { 'L', 21 },
            { 'M', 22 },
            { 'N', 23 },
            { 'O', 24 },
            { 'P', 25 },
            { 'Q', 26 },
            { 'R', 27 },
            { 'S', 28 },
            { 'T', 29 },
            { 'U', 30 },
            { 'V', 31 },
            { 'W', 32 },
            { 'X', 33 },
            { 'Y', 34 },
            { 'Z', 35 },

            { ' ', 36 },
            { '$', 37 },
            { '%', 38 },
            { '*', 39 },
            { '+', 40 },
            { '-', 41 },
            { '.', 42 },
            { '/', 43 },
            { ':', 44 },
        };

        #endregion Data
    }
}
