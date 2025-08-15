using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using UnboundLib.GameModes;

namespace SimultaneousCardPicksGM {
    public class Utils {
        public static FieldInfo FindNestedField(MethodInfo method, string fieldName) {
            if(method == null) throw new ArgumentNullException(nameof(method));

            Type declaringType = method.DeclaringType;
            if(declaringType == null) return null;

            foreach(Type nestedType in declaringType.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic)) {
                var attr = nestedType.GetCustomAttribute<CompilerGeneratedAttribute>();
                if(attr == null) continue;

                if(!nestedType.Name.Contains(method.Name)) continue;

                FieldInfo found = FindNestedFieldRecursive(nestedType, fieldName);
                if(found != null)
                    return found;
            }

            return null;
        }

        private static FieldInfo FindNestedFieldRecursive(Type type, string fieldName) {
            FieldInfo field = type.GetField(fieldName,
                BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
            if(field != null)
                return field;

            foreach(Type nested in type.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic)) {
                FieldInfo nestedField = FindNestedFieldRecursive(nested, fieldName);
                if(nestedField != null)
                    return nestedField;
            }

            return null;
        }
    }
}
