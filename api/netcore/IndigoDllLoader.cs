﻿using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Resources;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;

namespace com.epam.indigo
{
    class LibraryLoader
    {

        class WindowsLoader
        {
            [DllImport("kernel32")]
            public static extern IntPtr LoadLibrary(string lpFileName);
            [DllImport("kernel32.dll")]
            public static extern int FreeLibrary(IntPtr module);
            [DllImport("kernel32.dll")]
            public static extern IntPtr GetProcAddress(IntPtr hModule, string procedureName);
            [DllImport("kernel32.dll")]
            public static extern int GetLastError();
        }

        class LinuxLoader
        {
            [DllImport("libdl.so.2")]
            public static extern IntPtr dlopen([MarshalAs(UnmanagedType.LPTStr)] string filename, int flags);
            [DllImport("libdl.so.2")]
            public static extern int dlclose(IntPtr handle);
            [DllImport("libdl.so.2")]
            public static extern IntPtr dlsym(IntPtr libraryPointer, string procedureName);
            [DllImport("libdl.so.2")]
            public static extern string dlerror();
        }


        class MacLoader
        {
            [DllImport("libdl.dylib")]
            public static extern IntPtr dlopen(string filename, int flags);
            [DllImport("libdl.dylib")]
            public static extern int dlclose(IntPtr handle);
            [DllImport("libdl.dylib")]
            public static extern IntPtr dlsym(IntPtr libraryPointer, string procedureName);
            [DllImport("libdl.dylib")]
            public static extern string dlerror();
        }


        public static IntPtr LoadLibrary(string filename)
        {
            switch (Environment.OSVersion.Platform)
            {
                case PlatformID.Win32NT:
                    return WindowsLoader.LoadLibrary(filename);
                case PlatformID.Unix:
                    if (IndigoDllLoader.isMac())
                    {
                        return MacLoader.dlopen(filename.Replace("\\", "/"), 0x8 | 0x1); // RTLD_GLOBAL | RTLD_NOW
                    }
                    else
                    {
                        return LinuxLoader.dlopen(filename.Replace("\\", "/"), 0x00100 | 0x00002); // RTLD_GLOBAL | RTLD_NOW
                    }
            }
            return IntPtr.Zero;
        }

        public static int FreeLibrary(IntPtr handle)
        {
            switch (Environment.OSVersion.Platform)
            {
                case PlatformID.Win32NT:
                    return WindowsLoader.FreeLibrary(handle);
                case PlatformID.Unix:
                    if (IndigoDllLoader.isMac())
                    {
                        return MacLoader.dlclose(handle);
                    }
                    else
                    {
                        return LinuxLoader.dlclose(handle);
                    }
            }
            return 0;
        }

        public static IntPtr GetProcAddress(IntPtr library, string procedureName)
        {
            switch (Environment.OSVersion.Platform)
            {
                case PlatformID.Win32NT:
                    return WindowsLoader.GetProcAddress(library, procedureName);
                case PlatformID.Unix:
                    if (IndigoDllLoader.isMac())
                    {
                        return MacLoader.dlsym(library, procedureName);
                    }
                    else
                    {
                        return LinuxLoader.dlsym(library, procedureName);
                    }
            }
            return IntPtr.Zero;
        }

        public static string GetLastError()
        {
            switch (Environment.OSVersion.Platform)
            {
                case PlatformID.Win32NT:
                    return WindowsLoader.GetLastError().ToString();
                case PlatformID.Unix:
                    if (IndigoDllLoader.isMac())
                    {
                        return MacLoader.dlerror();
                    }
                    else
                    {
                        return LinuxLoader.dlerror();
                    }
            }
            return null;
        }
    }


    // Singleton DLL loader
    public class IndigoDllLoader
    {
        private static volatile IndigoDllLoader _instance;
        private static object _global_sync_root = new Object();
        private static volatile int _instance_id = 0;

        public static IndigoDllLoader Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_global_sync_root)
                    {
                        if (_instance == null)
                            _instance = new IndigoDllLoader();
                    }
                }

                return _instance;
            }
        }

        // Returns Id of the instance. When DllLoader is being finalized this value gets increased.
        public static int InstanceId
        {
            get
            {
                return _instance_id;
            }
        }

        class WrappedInterface
        {
            // Dictionary with delegates for calling unmanaged functions
            public Dictionary<string, Delegate> delegates = new Dictionary<string, Delegate>();
            // Interface instance with wrappers for calling unmanaged functions
            public object instance = null;
        }

        class DllData
        {
            public IntPtr handle;
            public string file_name;
            public string lib_path;

            public Dictionary<Type, WrappedInterface> interfaces = new Dictionary<Type, WrappedInterface>();
        }

        // Mapping from the DLL name to the handle.
        Dictionary<string, DllData> _loaded_dlls = new Dictionary<string, DllData>();
        // DLL handles in the loading order
        List<DllData> _dll_handles = new List<DllData>();
        // Local synchronization object
        Object _sync_object = new Object();

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
        struct utsname
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public string sysname;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public string nodename;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public string release;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public string version;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public string machine;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 1024)]
            public string extraJustInCase;

        }

        [DllImport("libc")]
        private static extern void uname(out utsname uname_struct);

        private static string detectUnixKernel()
        {
            utsname uts = new utsname();
            uname(out uts);
            return uts.sysname.ToString();
        }

        static public bool isMac()
        {
            return (detectUnixKernel() == "Darwin");
        }

        public void loadLibrary(string path, string filename)
        {
            lock (_sync_object)
            {
                DllData data = null;
                if (_loaded_dlls.TryGetValue(filename, out data))
                {
                    // Library has already been loaded
                    if (data.lib_path != path)
                        throw new IndigoException(string.Format("Library {0} has already been loaded by different path {1}", path, data.lib_path));
                    return;
                }
               
                data = new DllData();
                data.lib_path = path;
                data.file_name = _getPathToBinary(path, filename);
                data.handle = LibraryLoader.LoadLibrary(data.file_name.Replace('/', '\\'));
                if (data.handle == IntPtr.Zero)
                    throw new Exception(string.Format("Cannot load library {0} from the temporary file {1}: {2}",
                                                      filename, data.file_name.Replace('\\', '/'), LibraryLoader.GetLastError()));

                _loaded_dlls.Add(filename, data);

                _dll_handles.Add(data);
            }
        }

        ~IndigoDllLoader()
        {
            lock (_global_sync_root)
            {
                _instance = null;
                _instance_id++;

                // Unload all loaded libraries in the reverse order
                _dll_handles.Reverse();
                foreach (DllData dll in _dll_handles)
                    LibraryLoader.FreeLibrary(dll.handle);
            }
        }

        public bool isValid()
        {
            return (_instance != null);
        }

        string _getPathToBinary(string path, string filename)
        {
            return _extractFromAssembly(path, filename);
        }

        string _getTemporaryDirectory(Assembly resource_assembly)
        {
            string dir;
            dir = Path.Combine(Path.GetTempPath(), "EPAM_indigo");
            dir = Path.Combine(dir, resource_assembly.GetName().Name);
            dir = Path.Combine(dir, resource_assembly.GetName().Version.ToString());
            return dir;
        }

        string _extractFromAssembly(string inputPath, string filename)
        {
            string resource = string.Format("{0}.{1}", inputPath, filename);
            Stream fs = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource);
            if (fs == null)
                throw new IndigoException("Internal error: there is no resource " + resource);
            byte[] ba = new byte[fs.Length];
            fs.Read(ba, 0, ba.Length);
            fs.Close();
            if (ba == null)
                throw new IndigoException("Internal error: there is no resource " + resource);

            string tmpdir_path = _getTemporaryDirectory(Assembly.GetCallingAssembly());
            // Make per-version-unique dependent dll name
            string outputPath = Path.Combine(tmpdir_path, inputPath, filename);
            string dir = Path.GetDirectoryName(outputPath);
            string name = Path.GetFileName(outputPath);

            string new_dll_name = name;

            // This temporary file is used to avoid inter-process
            // race condition when concurrently stating many processes
            // on the same machine for the first time.
            string tmp_filename = Path.GetTempFileName();
            string new_full_path = Path.Combine(dir, new_dll_name);
            FileInfo file = new FileInfo(new_full_path);
            file.Directory.Create();
            // Check if file already exists
            if (!file.Exists || file.Length == 0) {
                File.WriteAllBytes(tmp_filename, ba);
                // file is ready to be moved.. lets check again
                if (!file.Exists || file.Length == 0) {
                    File.Move(tmp_filename, file.FullName);
                } else {
                    File.Delete(tmp_filename);
                }
            }
            return file.FullName;
        }

        // Returns implementation of a given interface for wrapping function the specified DLL
        public IT getInterface<IT>(string dll_name) where IT : class
        {
            lock (_sync_object)
            {
                Type itype = typeof(IT);
                // Check if such interface was already loaded
                WrappedInterface interf = null;
                if (_loaded_dlls.ContainsKey(dll_name))
                {
                    if (!_loaded_dlls[dll_name].interfaces.TryGetValue(itype, out interf))
                    {
                        interf = createInterface<IT>(dll_name);
                        _loaded_dlls[dll_name].interfaces.Add(itype, interf);
                    }
                }
                else
                {
                    interf = createInterface<IT>(dll_name);
                    _loaded_dlls[dll_name].interfaces.Add(itype, interf);
                }

                return (IT)interf.instance;
            }
        }

        string getDelegateField(MethodInfo m)
        {
            return m.Name + "_ptr";
        }

        Type createDelegateType(string delegate_type_name, ModuleBuilder mb, Type ret_type, Type[] arg_types)
        {
            // Create delegate
            TypeBuilder delegate_type = mb.DefineType(delegate_type_name,
               TypeAttributes.Class | TypeAttributes.Public | TypeAttributes.Sealed |
               TypeAttributes.AnsiClass | TypeAttributes.AutoClass,
               typeof(System.MulticastDelegate));

            ConstructorBuilder constructorBuilder =
               delegate_type.DefineConstructor(MethodAttributes.RTSpecialName |
               MethodAttributes.HideBySig | MethodAttributes.Public,
               CallingConventions.Standard,
               new Type[] { typeof(object), typeof(System.IntPtr) });
            constructorBuilder.SetImplementationFlags(MethodImplAttributes.Runtime | MethodImplAttributes.Managed);
            MethodBuilder methodBuilder = delegate_type.DefineMethod("Invoke",
               MethodAttributes.Public | MethodAttributes.HideBySig |
               MethodAttributes.NewSlot | MethodAttributes.Virtual,
               ret_type, arg_types);
            methodBuilder.SetImplementationFlags(MethodImplAttributes.Runtime | MethodImplAttributes.Managed);

            // Add [UnmanagedFunctionPointer(CallingConvention.Cdecl)] attribute for the delegate
            ConstructorInfo func_pointer_constructor =
               typeof(UnmanagedFunctionPointerAttribute).GetConstructor(new Type[] { typeof(CallingConvention) });
            CustomAttributeBuilder ca_builder =
               new CustomAttributeBuilder(func_pointer_constructor, new object[] { CallingConvention.Cdecl });
            delegate_type.SetCustomAttribute(ca_builder);

            return delegate_type.CreateType();
        }

        private class TypeListComparer : IEqualityComparer<List<Type>>
        {
            public bool Equals(List<Type> x, List<Type> y)
            {
                if (x.Count != y.Count)
                    return false;
                for (int i = 0; i < x.Count; i++)
                    if (x[i] != y[i])
                        return false;
                return true;
            }
            public int GetHashCode(List<Type> obj)
            {
                int hash = 0;
                foreach (Type t in obj)
                    hash ^= t.GetHashCode();
                return hash;
            }
        }


        // Creates implementation of a given interface for wrapping function the specified DLL
        WrappedInterface createInterface<IT>(string dll_name) where IT : class
        {
            WrappedInterface result = new WrappedInterface();

            Type itype = typeof(IT);
            AppDomain cd = System.Threading.Thread.GetDomain();
            AssemblyName an = new AssemblyName();
            an.Name = itype.Name + "_" + dll_name.Replace('.', '_');
            AssemblyBuilder ab = AssemblyBuilder.DefineDynamicAssembly(an, AssemblyBuilderAccess.Run);
            ModuleBuilder mb = ab.DefineDynamicModule(an.Name);
            TypeBuilder tb = mb.DefineType(an.Name, TypeAttributes.Class |
               TypeAttributes.Public);
            tb.AddInterfaceImplementation(itype);

            IntPtr dll_handle = _loaded_dlls[dll_name].handle;

            Dictionary<List<Type>, Type> signature_to_name =
               new Dictionary<List<Type>, Type>(new TypeListComparer());

            // Set delegate references
            foreach (MethodInfo m in itype.GetMethods())
            {
                ParameterInfo[] parameters = m.GetParameters();
                Type[] arg_types = new Type[parameters.Length];
                for (int i = 0; i < parameters.Length; i++)
                    arg_types[i] = parameters[i].ParameterType;

                Type delegate_ret_type = m.ReturnType;
                if (delegate_ret_type == typeof(string))
                    delegate_ret_type = typeof(sbyte*);

                List<Type> signature = new List<Type>();
                signature.Add(delegate_ret_type);
                signature.AddRange(arg_types);

                Type call_delegate = null;
                if (!signature_to_name.TryGetValue(signature, out call_delegate))
                {
                    // Check if type was already created
                    string delegate_type_name = string.Format("delegate_{0}", signature_to_name.Count);
                    call_delegate = createDelegateType(delegate_type_name, mb, delegate_ret_type, arg_types);
                    signature_to_name.Add(signature, call_delegate);
                }

                string delegate_field_name = m.Name + "_ptr";
                FieldBuilder delegate_field =
                   tb.DefineField(delegate_field_name, typeof(Delegate), FieldAttributes.Private);

                IntPtr proc = LibraryLoader.GetProcAddress(dll_handle, m.Name);
                if (proc == IntPtr.Zero)
                    throw new IndigoException(string.Format("Cannot find procedure {0} in the library {1}",
                       m.Name, dll_name));
                Delegate proc_delegate = Marshal.GetDelegateForFunctionPointer(proc, call_delegate);
                result.delegates.Add(delegate_field_name, proc_delegate);

                MethodBuilder meth = tb.DefineMethod(m.Name,
                   MethodAttributes.Public | MethodAttributes.Virtual, m.ReturnType, arg_types);

                ILGenerator il = meth.GetILGenerator();
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldfld, delegate_field);
                for (int i = 1; i < arg_types.Length + 1; i++)
                    il.Emit(OpCodes.Ldarg, i);
                MethodInfo infoMethod = proc_delegate.GetType().GetMethod("Invoke", arg_types);
                il.EmitCall(OpCodes.Callvirt, infoMethod, null);
                // Automatically convert sbyte* to string
                if (m.ReturnType == typeof(string))
                {
                    Type str_type = typeof(string);
                    ConstructorInfo ci = str_type.GetConstructor(new Type[] { typeof(sbyte*) });
                    il.Emit(OpCodes.Newobj, ci);
                }
                il.Emit(OpCodes.Ret);

                tb.DefineMethodOverride(meth, m);
            }

            // ab.Save(an.Name + ".dll");

            Type impl_class = tb.CreateType();
            IT impl = (IT)Activator.CreateInstance(impl_class);
            // Set references to the delegates
            foreach (string field_name in result.delegates.Keys)
            {
                impl_class.GetField(field_name, BindingFlags.Instance | BindingFlags.NonPublic)
                   .SetValue(impl, result.delegates[field_name]);
            }

            result.instance = impl;
            return result;
        }
    }
}
