﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Mono.Cecil;
using Mono.Collections.Generic;

using TypeAttributes = Mono.Cecil.TypeAttributes;

namespace Prism.Injector
{
    struct AsmInfo : IEquatable<AsmInfo>
    {
        readonly static string MODULE = "<Module>";
        readonly static string COMPILER_GENERATED = typeof(CompilerGeneratedAttribute).FullName;

        public readonly AssemblyDefinition assembly;
        public readonly List<TypeDefinition> types;

        public AsmInfo(AssemblyDefinition def)
        {
            assembly = def;

            types = def.Modules.Select(m =>
            {
                var t = FilterCompilerGenerated(m.Types);

                return t.SafeConcat(t.Select(GetNestedTypesRec).Flatten());
            }).Flatten().OrderBy(td => td.FullName).ToList();
        }

        public override bool Equals(object obj)
        {
            if (!(obj is AsmInfo))
                return false;

            return Equals((AsmInfo)obj);
        }
        public override int GetHashCode()
        {
            return assembly.GetHashCode() | types.GetHashCode();
        }
        public override string ToString()
        {
            return assembly.ToString();
        }

        public bool Equals(AsmInfo other)
        {
            return assembly == other.assembly && types == other.types;
        }

        static IEnumerable<TypeDefinition> FilterCompilerGenerated(IEnumerable<TypeDefinition> coll)
        {
            return coll.Where(td => (td.Attributes & (TypeAttributes.RTSpecialName | TypeAttributes.SpecialName)) == 0
                    && td.Name != MODULE && !td.CustomAttributes.Any(ca => ca.AttributeType.FullName == COMPILER_GENERATED));
        }
        static IEnumerable<TypeDefinition> GetNestedTypesRec(TypeDefinition d)
        {
            if (!d.HasNestedTypes)
                return null;

            var n = FilterCompilerGenerated(d.NestedTypes);
            return n.SafeConcat(n.Select(GetNestedTypesRec).Flatten());
        }

        public static bool operator ==(AsmInfo a, AsmInfo b)
        {
            return a.Equals(b);
        }
        public static bool operator !=(AsmInfo a, AsmInfo b)
        {
            return a.assembly != b.assembly || a.types != b.types;
        }
    }

    public class CecilContext
    {
        internal AsmInfo primaryAssembly;
        Assembly reflectionOnlyAsm;

        public AssemblyDefinition PrimaryAssembly
        {
            get
            {
                return primaryAssembly.assembly;
            }
        }

        public AssemblyNameReference[] References
        {
            //get
            //{
            //    return referencedAssemblies;
            //}
            get;
            private set;
        }

        public CecilReflectionComparer Comparer
        {
            get;
            private set;
        }
        public MemberResolver Resolver
        {
            get;
            private set;
        }

        public CecilContext(string asmToLoad)
        {
            var pa = AssemblyDefinition.ReadAssembly(asmToLoad);

            reflectionOnlyAsm = Assembly.ReflectionOnlyLoadFrom(asmToLoad);

            var refs = reflectionOnlyAsm.GetReferencedAssemblies();
            References = refs.Select(TranslateReference).ToArray();

            //stdLibAsms = refs.Where(n =>
            //{
            //    try
            //    {
            //        return Assembly.ReflectionOnlyLoad(n.FullName).GlobalAssemblyCache;
            //    }
            //    catch
            //    {
            //        return false;
            //    }
            //}).Select(TranslateReference).Select(n => new AsmInfo(pa.MainModule.AssemblyResolver.Resolve(n))).ToList();

            primaryAssembly = new AsmInfo(pa); // load types after stdlib/gac references are loaded

            Comparer = new CecilReflectionComparer(this);
            Resolver = new MemberResolver       (this);
        }

        AssemblyNameReference TranslateReference(AssemblyName name)
        {
            var anr = new AssemblyNameReference(name.Name, name.Version);

            anr.Attributes = (AssemblyAttributes)name.Flags;
            anr.Culture = name.CultureInfo.Name;
            anr.HashAlgorithm = (AssemblyHashAlgorithm)name.HashAlgorithm;
            anr.PublicKey = name.GetPublicKey();
            anr.PublicKeyToken = name.GetPublicKeyToken();

            return anr;
        }
    }
}
