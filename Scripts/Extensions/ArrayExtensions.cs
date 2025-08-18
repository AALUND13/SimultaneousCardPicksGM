using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SimultaneousCardPicksGM.Extensions {
    public static class ArrayExtensions {
        public static T[] Shift<T>(this T[] array, int shift) {
            if (array == null || array.Length == 0) return array;
            int length = array.Length;
            shift = shift % length;
            if (shift < 0) {
                shift += length;
            }
            T[] result = new T[length];
            Array.Copy(array, length - shift, result, 0, shift);
            Array.Copy(array, 0, result, shift, length - shift);
            return result;
        }
    }
}
