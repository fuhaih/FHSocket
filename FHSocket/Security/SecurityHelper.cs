using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Security;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Runtime.Serialization.Formatters.Binary;
namespace FHSocket.Security
{
    public static class SecurityHelper
    {
        /// <summary>
        /// 获取string的MD5
        /// </summary>
        /// <param name="t"></param>
        /// <returns></returns>
        public static byte[] ToMD5(this string t)
        {
            byte[] buffer= Encoding.Default.GetBytes(t);
            MD5 md5 = new MD5CryptoServiceProvider();
            byte[] retVal = md5.ComputeHash(buffer);
            //StringBuilder sb = new StringBuilder();
            //for (int i = 0; i < retVal.Length; i++)
            //{
            //    sb.Append(retVal[i].ToString("x2"));
            //}
            //result = sb.ToString();
            return retVal;
        }

        /// <summary>
        /// 计算HMACMD5哈希序列
        /// </summary>
        /// <param name="t"></param>
        /// <param name="key"></param>
        /// <returns></returns>
        public static byte[] ToHMACMD5(this string t,string key)
        {
            HMACMD5 MDS = new HMACMD5(Encoding.UTF8.GetBytes(key));
            byte[] buffer = MDS.ComputeHash(Encoding.UTF8.GetBytes(t));
            //StringBuilder sb = new StringBuilder();
            //for (int i = 0; i < buffer.Length; i++)
            //{
            //    sb.Append(buffer[i].ToString("x2"));
            //}
            //string result = sb.ToString();
            return buffer;
        }

        /// <summary>
        /// Aes加密
        /// </summary>
        /// <param name="source">源字符串</param>
        /// <param name="key">aes密钥，长度必须32位</param>
        /// <returns>加密后的字符串</returns>
        public static byte[] EncryptAes(byte[] source, string key, string iv)
        {
            using (AesCryptoServiceProvider aesProvider = new AesCryptoServiceProvider())
            {
                aesProvider.Key = Encoding.UTF8.GetBytes(key);
                aesProvider.Mode = CipherMode.CBC;
                aesProvider.IV = Encoding.UTF8.GetBytes(iv);
                aesProvider.Padding = PaddingMode.PKCS7;
                using (ICryptoTransform cryptoTransform = aesProvider.CreateEncryptor())
                {
                    byte[] results = cryptoTransform.TransformFinalBlock(source, 0, source.Length);
                    aesProvider.Clear();
                    aesProvider.Dispose();
                    //return Convert.ToBase64String(results, 0, results.Length);
                    return results;
                }
            }
        }

        /// <summary>
        /// Aes解密
        /// </summary>
        /// <param name="source">源字符串</param>
        /// <param name="key">aes密钥，长度必须32位</param>
        /// <returns>解密后的字符串</returns>
        public static byte[] DecryptAes(byte[] source, string key, string iv)
        {
            using (AesCryptoServiceProvider aesProvider = new AesCryptoServiceProvider())
            {
                aesProvider.Key = Encoding.UTF8.GetBytes(key);
                aesProvider.IV = Encoding.UTF8.GetBytes(iv);
                aesProvider.Mode = CipherMode.CBC;
                aesProvider.Padding = PaddingMode.PKCS7;
                using (ICryptoTransform cryptoTransform = aesProvider.CreateDecryptor())
                {
                    byte[] results = cryptoTransform.TransformFinalBlock(source, 0, source.Length);
                    aesProvider.Clear();
                    return results;
                }
            }
        }
    }
}
