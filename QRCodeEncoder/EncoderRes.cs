// Алгоритм генерации QR-кода
// https://habr.com/ru/post/172525/

namespace QRCodeEncoder
{
    public class EncoderRes
    {
        public byte[] Data { get; set; }
        
        public int Version { get; set; }
    }
}
