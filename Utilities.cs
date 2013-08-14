using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace Chordian
{
    class Utilities
    {
        public static UInt64 GetMD5Hash(string key)
        {
            MD5CryptoServiceProvider md5 = new MD5CryptoServiceProvider();
            byte[] bytes = Encoding.ASCII.GetBytes(key);
            bytes = md5.ComputeHash(bytes);
            return BitConverter.ToUInt64(bytes, 0);
        }

        public static bool IsFingerInRange(UInt64 key, UInt64 startKey, UInt64 endKey)
        {
            return startKey == endKey || ((startKey > endKey) && (key > startKey || key < endKey)) || (key > startKey && key < endKey);
        }

        public static bool IsKeyInRange(UInt64 key, UInt64 startKey, UInt64 endKey)
        {
            return (((startKey >= endKey) && (key > startKey || key <= endKey)) || (key > startKey && key <= endKey));
        }
    }
}
