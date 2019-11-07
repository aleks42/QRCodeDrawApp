using SkiaSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

// Алгоритм генерации QR-кода
// https://habr.com/ru/post/172525/

namespace QRCodeEncoder
{
    public class Renderer
    {
        public Stream Draw(byte[] bytes, int ver, CorrectionLevel level, Stream backgroudImg)
        {
            var img = GetQRCodeArray(bytes, ver, level);

            var scale = 10;
            var dx = 0;
            var dy = 0;

            int width = (img.GetUpperBound(0) + 1) * scale;
            int height = (img.GetUpperBound(0) + 1) * scale;

            SKBitmap bitmap2;

            if (backgroudImg != null)
            {
                using (var bitmap = SKBitmap.Decode(backgroudImg))
                    bitmap2 = bitmap.Resize(new SKImageInfo(width, height), SKFilterQuality.High);
            }
            else
            {
                bitmap2 = new SKBitmap(width, height);
            }

            using (var canvas = new SKCanvas(bitmap2))
            {
                if (backgroudImg == null) canvas.Clear(SKColors.Transparent);

                var paint1 = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill, Color = SKColors.DeepSkyBlue };
                var paint2 = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill, Color = SKColors.Red };
                var paint3 = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill, Color = SKColors.Gold };

                for (int x = 0; x <= img.GetUpperBound(0); x++)
                {
                    for (int y = 0; y <= img.GetUpperBound(0); y++)
                    {
                        if (img[x, y] == 0)
                        {
                            var markers = Markers[ver - 1];

                            if (x < 6 && y < 6
                                || x < 6 && y > img.GetUpperBound(0) - 6
                                || y < 6 && x > img.GetUpperBound(0) - 6)
                            {
                                // поисковые узоры
                                canvas.DrawRect(x * scale + dx, y * scale + dy, scale, scale, paint2);
                            }
                            else if (x < 8 && y < 8
                                || x < 8 && y > img.GetUpperBound(0) - 8
                                || y < 8 && x > img.GetUpperBound(0) - 8)
                            {
                                // контур поисковых узоров
                                canvas.DrawRect(x * scale + dx, y * scale + dy, scale, scale, paint1);
                            }
                            else if (markers?.Length > 0
                                && ver >= 2
                                && markers.Any(m => x >= m - 1 && x <= m + 1)
                                && markers.Any(m => y >= m - 1 && y <= m + 1))
                            {
                                // выравнивающие узоры
                                canvas.DrawRect(x * scale + dx, y * scale + dy, scale, scale, paint3);
                            }
                            else
                            {
                                canvas.DrawCircle(x * scale + scale / 2 + dx, y * scale + scale / 2 + dy, scale / 2, paint1);

                                // сглаживание линий
                                if (x > 0 && img[x - 1, y] == 0)
                                    canvas.DrawRect(x * scale + dx - scale / 2, y * scale + dy, scale, scale, paint1);
                                    
                                if (y > 0 && img[x, y - 1] == 0)
                                    canvas.DrawRect(x * scale + dx, y * scale + dy - scale / 2, scale, scale, paint1);
                            }
                        }
                    }
                }

                canvas.Flush();

                var pixMap = bitmap2.PeekPixels();
                var pngImage2 = pixMap.Encode(new SKPngEncoderOptions());
                var stream = pngImage2.AsStream();
                stream.Position = 0;
                return stream;
            }
        }

        //

        private byte[,] GetQRCodeArray(byte[] data, int ver, CorrectionLevel correctionLevel)
        {
            var size = Markers[ver - 1].Last() + 7;
            var img = new byte[size, size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    img[x, y] = 2;
                }
            }

            AddCornerMarks(img);
            AddAlignMarks(img, ver);
            AddSyncLines(img);
            AddVersionCode(img, ver);

            // выбор маски

            var bestScore = int.MaxValue;
            byte[,] bestMask = null;
            for (int maskNum = 0; maskNum <= 7; maskNum++)
            {
                var imgCopy1 = AddCorrectionAndMaskCode(img, maskNum, correctionLevel);
                var imgCopy2 = AddData(data, imgCopy1, maskNum);
                var itemScore = MaskScoring(imgCopy2);
                if (itemScore < bestScore)
                {
                    bestScore = itemScore;
                    bestMask = imgCopy2;
                }
            }

            return bestMask;
        }

        /// <summary>
        /// Выбор лучшей маски
        /// </summary>
        private int MaskScoring(byte[,] img) => MaskScoring1(img) + MaskScoring2(img) + MaskScoring3(img) + MaskScoring4(img);
        private int MaskScoring1(byte[,] img)
        {
            var score = 0;

            // rule 1
            // По горизонтали и вертикали за каждые 5 и больше идущих подряд модулей одного цвета начисляется количество очков, 
            // равное длине этого участка минус 2. В этом и во всех остальных правилах отступ не рассматривается, 
            // всё ограничивается основным полем.

            for (int y = 0; y <= img.GetUpperBound(0); y++)
            {
                var counter1 = 0;
                for (int x = 1; x <= img.GetUpperBound(0); x++)
                {
                    if (img[x - 1, y] == img[x, y])
                    {
                        counter1++;
                    }
                    else
                    {
                        if (counter1 >= 5)
                        {
                            score += counter1 - 2;
                        }
                        counter1 = 0;
                    }
                }
            }

            for (int x = 0; x <= img.GetUpperBound(0); x++)
            {
                var counter1 = 0;
                for (int y = 1; y <= img.GetUpperBound(0); y++)
                {
                    if (img[x, y - 1] == img[x, y])
                    {
                        counter1++;
                    }
                    else
                    {
                        if (counter1 >= 5)
                        {
                            score += counter1 - 2;
                        }
                        counter1 = 0;
                    }
                }
            }

            return score;
        }
        private int MaskScoring2(byte[,] img)
        {
            var score = 0;

            // rule 2
            // За каждый квадрат модулей одного цвета размером 2 на 2 начисляется по 3 очка.

            for (int y = 1; y <= img.GetUpperBound(0); y++)
            {
                for (int x = 1; x <= img.GetUpperBound(0); x++)
                {
                    if (new byte[] { img[x - 1, y], img[x, y - 1], img[x - 1, y - 1] }
                        .All(item => item == img[x, y]))
                    {
                        score += 3;
                    }
                }
            }

            return score;
        }
        private int MaskScoring3(byte[,] img)
        {
            var score = 0;

            // rule 3
            // За каждую последовательность модулей ЧБЧЧЧБЧ, с 4-мя белыми модулями с одной из сторон (или с 2-х сразу), 
            // добавляется 40 очков (по вертикали или горизонтали).

            var pattern1 = new byte[] { 0, 0, 0, 0, 1, 0, 1, 1, 1, 0, 1 };
            var pattern2 = new byte[] { 1, 0, 1, 1, 1, 0, 1, 0, 0, 0, 0 };
            var pattern3 = new byte[] { 0, 0, 0, 0, 1, 0, 1, 1, 1, 0, 1, 0, 0, 0, 0 };

            bool checkPatternX(byte[,] arr, byte[] pattern, int x, int y)
            {
                if (arr.GetUpperBound(0) < pattern.Length + x - 1) return false;
                for (int x1 = 0; x1 <= pattern.GetUpperBound(0); x1++)
                {
                    if (arr[x1 + x, y] != pattern[x1]) return false;
                }
                return true;
            }

            bool checkPatternY(byte[,] arr, byte[] pattern, int x, int y)
            {
                if (arr.GetUpperBound(0) < pattern.Length + y - 1) return false;
                for (int y1 = 0; y1 <= pattern.GetUpperBound(0); y1++)
                {
                    if (arr[x, y1 + y] != pattern[y1]) return false;
                }
                return true;
            }

            for (int y = 0; y <= img.GetUpperBound(0); y++)
            {
                for (int x = pattern1.Length - 1; x <= img.GetUpperBound(0); x++)
                {
                    if (checkPatternX(img, pattern1, x, y) 
                        || checkPatternX(img, pattern2, x, y) 
                        || checkPatternX(img, pattern3, x, y)) score += 40;
                }
            }

            for (int x = pattern1.Length - 1; x <= img.GetUpperBound(0); x++)
            {
                for (int y = 0; y <= img.GetUpperBound(0); y++)
                {
                    if (checkPatternY(img, pattern1, x, y) 
                        || checkPatternY(img, pattern2, x, y) 
                        || checkPatternY(img, pattern3, x, y)) score += 40;
                }
            }

            return score;
        }
        private int MaskScoring4(byte[,] img)
        {
            // rule 4
            // Количество очков на этом шаге зависит от соотношения количества чёрных и белых модулей. Чем ближе оно к соотношению 50% на 50%, тем лучше.

            int sum = img.Cast<byte>().Sum(x => x);
            return 2 * Math.Abs((int)(100 * sum / Math.Pow(img.GetUpperBound(0) + 1, 2) - 50));
        }

        /// <summary>
        /// Добавление данных
        /// </summary>
        private byte[,] AddData(byte[] data, byte[,] img1, int maskNum)
        {
            var img = img1.Clone() as byte[,];
            var bitString = string.Join("", data.Select(x1 => Convert.ToString(x1, 2).PadLeft(8, '0')).ToArray());

            var x = img.GetUpperBound(0);
            var y = img.GetUpperBound(0);

            var semiColumn = 1; // левая колонка 0, правая 1
            var direction = -1; // вверх -1, вниз 1

            var counter1 = 0;
            while (true)
            {
                if (img[x, y] == 2)
                {
                    if (counter1 < bitString.Length)
                    {
                        img[x, y] = bitString[counter1++] == '1' ? (byte)1 : (byte)0;
                        img[x, y] = AddMaskToModule(img[x, y], x, y, maskNum);
                    }
                    else
                    {
                        img[x, y] = AddMaskToModule(0, x, y, maskNum);
                    }
                }

                semiColumn = Math.Abs(semiColumn - 1);
                if (semiColumn == 0)
                {
                    x--;
                }
                else
                {
                    x++;
                    y += direction;

                    if (y < 0)
                    {
                        direction = 1; // вниз
                        y = 0;
                        x -= 2;
                    }
                    else if (y > img.GetUpperBound(0))
                    {
                        direction = -1; // вверх
                        y = img.GetUpperBound(0);
                        x -= 2;
                    }
                }

                if (x == 6) x--;

                if (x < 0) break;
            }

            return img;
        }

        /// <summary>
        /// Код маски и уровня коррекции
        /// </summary>
        private byte[,] AddCorrectionAndMaskCode(byte[,] img1, int maskNum, CorrectionLevel correctionLevel)
        {
            var img = img1.Clone() as byte[,];

            List<string> maskCodes;
            switch (correctionLevel)
            {
                case CorrectionLevel.L:
                    maskCodes = MaskCodesL;
                    break;
                case CorrectionLevel.M:
                default:
                    maskCodes = MaskCodesM;
                    break;
                case CorrectionLevel.Q:
                    maskCodes = MaskCodesQ;
                    break;
                case CorrectionLevel.H:
                    maskCodes = MaskCodesH;
                    break;
            }

            var counter = 0;
            for (int i = 0; i < 6; i++)
            {
                img[i, 8] = maskCodes[maskNum][counter] == '1' ? (byte)1 : (byte)0;
                img[8, img.GetUpperBound(0) - i] = maskCodes[maskNum][counter] == '1' ? (byte)1 : (byte)0;
                counter++;
            }

            img[7, 8] = maskCodes[maskNum][counter] == '1' ? (byte)1 : (byte)0;
            img[8, img.GetUpperBound(0) - 6] = maskCodes[maskNum][counter] == '1' ? (byte)1 : (byte)0;
            counter++;

            img[8, 8] = maskCodes[maskNum][counter] == '1' ? (byte)1 : (byte)0;
            img[8, img.GetUpperBound(0) - 7] = 1; // всегда чёрный
            img[img.GetUpperBound(0) - 7, 8] = maskCodes[maskNum][counter] == '1' ? (byte)1 : (byte)0;
            counter++;

            img[8, 7] = maskCodes[maskNum][counter] == '1' ? (byte)1 : (byte)0;
            img[img.GetUpperBound(0) - 6, 8] = maskCodes[maskNum][counter] == '1' ? (byte)1 : (byte)0;
            counter++;

            for (int i = 5; i >= 0; i--)
            {
                img[8, i] = maskCodes[maskNum][counter] == '1' ? (byte)1 : (byte)0;
                img[img.GetUpperBound(0) - i, 8] = maskCodes[maskNum][counter] == '1' ? (byte)1 : (byte)0;
                counter++;
            }

            return img;
        }

        /// <summary>
        /// Код версии
        /// </summary>
        private void AddVersionCode(byte[,] img, int ver)
        {
            if (ver >= 7)
            {
                var varCode = VersionCodes[ver - 1];

                var counter = 0;
                var dy = img.GetUpperBound(0) - 10;

                for (int i2 = 0; i2 < 3; i2++)
                {
                    for (int i1 = 0; i1 < 6; i1++)
                    {
                        img[i1, i2 + dy] = varCode[counter] == '1' ? (byte)1 : (byte)0;
                        img[i2 + dy, i1] = varCode[counter] == '1' ? (byte)1 : (byte)0;
                        counter++;
                    }
                }
            }
        }

        /// <summary>
        /// Полосы синхронизации
        /// </summary>
        private void AddSyncLines(byte[,] img)
        {
            for (int i = 8; i < img.GetUpperBound(0) - 7; i++)
            {
                if (i % 2 == 0)
                {
                    img[i, 6] = 1;
                    img[6, i] = 1;
                }
                else
                {
                    img[i, 6] = 0;
                    img[6, i] = 0;
                }
            }
        }

        /// <summary>
        /// Поисковые узоры
        /// </summary>
        private void AddCornerMarks(byte[,] img)
        {
            var mark1 = new byte[8, 8]
            {
                { 1, 1, 1, 1, 1, 1, 1, 0},
                { 1, 0, 0, 0, 0, 0, 1, 0},
                { 1, 0, 1, 1, 1, 0, 1, 0},
                { 1, 0, 1, 1, 1, 0, 1, 0},
                { 1, 0, 1, 1, 1, 0, 1, 0},
                { 1, 0, 0, 0, 0, 0, 1, 0},
                { 1, 1, 1, 1, 1, 1, 1, 0},
                { 0, 0, 0, 0, 0, 0, 0, 0}
            };
            DrawBytes(img, mark1, 0, 0);

            var mark2 = new byte[8, 8]
            {
                { 0, 0, 0, 0, 0, 0, 0, 0},
                { 1, 1, 1, 1, 1, 1, 1, 0},
                { 1, 0, 0, 0, 0, 0, 1, 0},
                { 1, 0, 1, 1, 1, 0, 1, 0},
                { 1, 0, 1, 1, 1, 0, 1, 0},
                { 1, 0, 1, 1, 1, 0, 1, 0},
                { 1, 0, 0, 0, 0, 0, 1, 0},
                { 1, 1, 1, 1, 1, 1, 1, 0}
            };
            DrawBytes(img, mark2, img.GetUpperBound(0) - 7, 0);

            var mark3 = new byte[8, 8]
            {
                { 0, 1, 1, 1, 1, 1, 1, 1},
                { 0, 1, 0, 0, 0, 0, 0, 1},
                { 0, 1, 0, 1, 1, 1, 0, 1},
                { 0, 1, 0, 1, 1, 1, 0, 1},
                { 0, 1, 0, 1, 1, 1, 0, 1},
                { 0, 1, 0, 0, 0, 0, 0, 1},
                { 0, 1, 1, 1, 1, 1, 1, 1},
                { 0, 0, 0, 0, 0, 0, 0, 0}
            };
            DrawBytes(img, mark3, 0, img.GetUpperBound(0) - 7);
        }

        /// <summary>
        /// Выравнивающие узоры
        /// </summary>
        private void AddAlignMarks(byte[,] img, int ver)
        {
            var mark = new byte[5, 5]
            {
                { 1, 1, 1, 1, 1 },
                { 1, 0, 0, 0, 1 },
                { 1, 0, 1, 0, 1 },
                { 1, 0, 0, 0, 1 },
                { 1, 1, 1, 1, 1 }
            };

            var marks = Markers[ver - 1];

            if (ver == 1)
            {
                return;
            }
            if (ver <= 6)
            {
                DrawBytes(img, mark, marks[0] - 2, marks[0] - 2);
            }
            else
            {
                for (int i1 = 0; i1 < marks.Length; i1++)
                {
                    for (int i2 = 0; i2 < marks.Length; i2++)
                    {
                        if (i1 == 0)
                        {
                            if (i2 != 0 && i2 < marks.Length - 1)
                            {
                                DrawBytes(img, mark, marks[i1] - 2, marks[i2] - 2);
                            }
                        }
                        else if (i2 == 0)
                        {
                            if (i1 != 0 && i1 < marks.Length - 1)
                            {
                                DrawBytes(img, mark, marks[i1] - 2, marks[i2] - 2);
                            }
                        }
                        else
                        {
                            DrawBytes(img, mark, marks[i1] - 2, marks[i2] - 2);
                        }
                    }
                }
            }
        }

        private void DrawBytes(byte[,] canvas, byte[,] img, int x, int y)
        {
            for (int i1 = 0; i1 <= img.GetUpperBound(0); i1++)
            {
                for (int i2 = 0; i2 <= img.GetUpperBound(1); i2++)
                {
                    if (i1 + x <= canvas.GetUpperBound(0)
                        && i2 + y <= canvas.GetUpperBound(1))
                        canvas[i1 + x, i2 + y] = img[i1, i2];
                }
            }
        }

        /// <summary>
        /// Маска
        /// </summary>
        private byte AddMaskToModule(byte module, int x, int y, int maskNum)
        {
            var res = 0;
            switch (maskNum)
            {
                case 0:
                default:
                    res = (x + y) % 2;
                    break;
                case 1:
                    res = y % 2;
                    break;
                case 2:
                    res = x % 3;
                    break;
                case 3:
                    res = (x + y) % 3;
                    break;
                case 4:
                    res = (x / 3 + y / 2) % 2;
                    break;
                case 5:
                    res = x * y % 2 + x * y % 3;
                    break;
                case 6:
                    res = (x * y % 2 + x * y % 3) % 2;
                    break;
                case 7:
                    res = (x * y % 3 + (x + y) % 2) % 2;
                    break;
            }

            if (res == 0 && module == 0) module = 1;
            else if (res == 0 && module == 1) module = 0;
            return module;
        }

        #region Data

        /// <summary>
        /// Расположение выравнивающих узоров по версии
        /// </summary>
        public static readonly List<int[]> Markers = new List<int[]>
        {
            new int[] { 14 },
            new int[] { 18 },
            new int[] { 22 },
            new int[] { 26 },
            new int[] { 30 },
            new int[] { 34 },
            new int[] { 6, 22, 38 },
            new int[] { 6, 24, 42 },
            new int[] { 6, 26, 46 },
            new int[] { 6, 28, 50 },
            new int[] { 6, 30, 54 },
            new int[] { 6, 32, 58 },
            new int[] { 6, 34, 62 },
            new int[] { 6, 26, 46, 66 },
            new int[] { 6, 26, 48, 70 },
            new int[] { 6, 26, 50, 74 },
            new int[] { 6, 30, 54, 78 },
            new int[] { 6, 30, 56, 82 },
            new int[] { 6, 30, 58, 86 },
            new int[] { 6, 34, 62, 90 },
            new int[] { 6, 28, 50, 72, 94 },
            new int[] { 6, 26, 50, 74, 98 },
            new int[] { 6, 30, 54, 78, 102 },
            new int[] { 6, 28, 54, 80, 106 },
            new int[] { 6, 32, 58, 84, 110 },
            new int[] { 6, 30, 58, 86, 114 },
            new int[] { 6, 34, 62, 90, 118 },
            new int[] { 6, 26, 50, 74, 98, 122 },
            new int[] { 6, 30, 54, 78, 102, 126 },
            new int[] { 6, 26, 52, 78, 104, 130 },
            new int[] { 6, 30, 56, 82, 108, 134 },
            new int[] { 6, 34, 60, 86, 112, 138 },
            new int[] { 6, 30, 58, 86, 114, 142 },
            new int[] { 6, 34, 62, 90, 118, 146 },
            new int[] { 6, 30, 54, 78, 102, 126, 150 },
            new int[] { 6, 24, 50, 76, 102, 128, 154 },
            new int[] { 6, 28, 54, 80, 106, 132, 158 },
            new int[] { 6, 32, 58, 84, 110, 136, 162 },
            new int[] { 6, 26, 54, 82, 110, 138, 166 },
            new int[] { 6, 30, 58, 86, 114, 142, 170 },
        };

        /// <summary>
        /// Код маски и уровня коррекции, уровень коррекции L
        /// </summary>
        public static readonly List<string> MaskCodesL = new List<string>
        {
            "111011111000100",
            "111001011110011",
            "111110110101010",
            "111100010011101",
            "110011000101111",
            "110001100011000",
            "110110001000001",
            "110100101110110"
        };

        /// <summary>
        /// Код маски и уровня коррекции, уровень коррекции M
        /// </summary>
        public static readonly List<string> MaskCodesM = new List<string>
        {
            "101010000010010",
            "101000100100101",
            "101111001111100",
            "101101101001011",
            "100010111111001",
            "100000011001110",
            "100111110010111",
            "100101010100000"
        };

        /// <summary>
        /// Код маски и уровня коррекции, уровень коррекции Q
        /// </summary>
        public static readonly List<string> MaskCodesQ = new List<string>
        {
            "011010101011111",
            "011000001101000",
            "011111100110001",
            "011101000000110",
            "010010010110100",
            "010000110000011",
            "010111011011010",
            "010101111101101"
        };

        /// <summary>
        /// Код маски и уровня коррекции, уровень коррекции H
        /// </summary>
        public static readonly List<string> MaskCodesH = new List<string>
        {
            "001011010001001",
            "001001110111110",
            "001110011100111",
            "001100111010000",
            "000011101100010",
            "000001001010101",
            "000110100001100",
            "000100000111011"
        };

        /// <summary>
        /// Коды версий
        /// </summary>
        public static readonly List<string> VersionCodes = new List<string>
        {
            "",
            "",
            "",
            "",
            "",
            "",
            "000010011110100110",
            "010001011100111000",
            "110111011000000100",
            "101001111110000000",
            "001111111010111100",
            "001101100100011010",
            "101011100000100110",
            "110101000110100010",
            "010011000010011110",
            "011100010001011100",
            "111010010101100000",
            "100100110011100100",
            "000010110111011000",
            "000000101001111110",
            "100110101101000010",
            "111000001011000110",
            "011110001111111010",
            "001101001101100100",
            "101011001001011000",
            "110101101111011100",
            "010011101011100000",
            "010001110101000110",
            "110111110001111010",
            "101001010111111110",
            "001111010011000010",
            "101000011000101101",
            "001110011100010001",
            "010000111010010101",
            "110110111110101001",
            "110100100000001111",
            "010010100100110011",
            "001100000010110111",
            "101010000110001011",
            "111001000100010101",
        };

        #endregion Data
    }
}
