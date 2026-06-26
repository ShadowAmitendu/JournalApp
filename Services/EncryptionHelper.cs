using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace JournalApp
{
    public static class EncryptionHelper
    {
        public static byte[] Encrypt(byte[] plainBytes, string password)
        {
            if (plainBytes == null) return null;
            if (string.IsNullOrEmpty(password)) return plainBytes;

            using var aes = Aes.Create();
            var key = SHA256.HashData(Encoding.UTF8.GetBytes(password));
            aes.Key = key;
            aes.GenerateIV();
            using var encryptor = aes.CreateEncryptor();
            var encryptedBytes = encryptor.TransformFinalBlock(plainBytes, 0, plainBytes.Length);
            
            // Return IV + Encrypted bytes combined
            var result = new byte[aes.IV.Length + encryptedBytes.Length];
            Buffer.BlockCopy(aes.IV, 0, result, 0, aes.IV.Length);
            Buffer.BlockCopy(encryptedBytes, 0, result, aes.IV.Length, encryptedBytes.Length);
            return result;
        }

        public static byte[] Decrypt(byte[] cipherBytes, string password)
        {
            if (cipherBytes == null) return null;
            if (string.IsNullOrEmpty(password)) return cipherBytes;
            if (cipherBytes.Length < 16) throw new Exception("Invalid data length for decryption");

            using var aes = Aes.Create();
            var key = SHA256.HashData(Encoding.UTF8.GetBytes(password));
            aes.Key = key;
            
            var iv = new byte[16];
            Buffer.BlockCopy(cipherBytes, 0, iv, 0, 16);
            aes.IV = iv;
            
            using var decryptor = aes.CreateDecryptor();
            return decryptor.TransformFinalBlock(cipherBytes, 16, cipherBytes.Length - 16);
        }
    }
}
