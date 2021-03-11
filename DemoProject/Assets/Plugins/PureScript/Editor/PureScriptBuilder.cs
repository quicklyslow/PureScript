﻿using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Build.Player;
using UnityEditor.Callbacks;
using UnityEngine;
using System.Reflection;
using System.Runtime.CompilerServices;
using System;
using System.Runtime.InteropServices;
using Unity.Collections.LowLevel.Unsafe;
using System.IO;
using System.Text;
using Process = System.Diagnostics.Process;

public static class PureScriptBuilder 
{
    public static bool Enable;
    static string ScriptEngineDir;
    static bool Inbuild = false;

    static PureScriptBuilder()
    {
        Enable = true;
        ScriptEngineDir = "../ScriptEngine";
#if UNITY_ANDROID
        Enable = false;
#endif
    }


    [UnityEditor.Callbacks.PostProcessScene]
    public static void AutoInjectAssemblys()
    {
        if (EditorApplication.isCompiling || Application.isPlaying)
            return;

        if (Enable && !Inbuild)
        {
            Inbuild = true;
            ScriptEngineDir = Path.GetFullPath(ScriptEngineDir);
            InsertBuildTask();
        }
    }

    /// <summary>
    /// bind adapter before strip.
    /// called by UnityEditor when AssemblyStripper.StripAssemblies.
    /// "replace assembly" after strip will trigger il2cpp.exe`s error.
    /// </summary>
    ///
    public static void RunBinderBeforeStrip(System.String managedAssemblyFolderPath)
    {
        Inbuild = false;
        var managedPath = Path.Combine(ScriptEngineDir, "Managed");

        //copy all assemblys
        CopyManagedFile(managedAssemblyFolderPath, managedPath);

        // call binder,bind adapter
        CallBinder("Adapter");

        // replace adapter by generated assembly
        var generatedAdapter = Path.Combine(managedPath, "Adapter.gen.dll");
        var adapterGenPath = Path.Combine(managedAssemblyFolderPath, "Adapter.gen.dll");
        if (File.Exists(generatedAdapter))
        {
            File.Copy(generatedAdapter, adapterGenPath, true);
            File.Delete(generatedAdapter);
        }
    }

    /// <summary>
    /// resolve all bind task after strip.
    /// called by UnityEditor when IL2CPPBuilder.RunIl2CppWithArguments.
    /// </summary>
    public static void RunBinderBeforeIl2cpp(string workingDirectory)
    {
        Inbuild = false;

        //copy all striped assemblys
        var managedPath = Path.Combine(ScriptEngineDir, "Managed");
        CopyManagedFile(workingDirectory, managedPath);

        // call binder,bind icall and adapter
        CallBinder("All");
    }

    public static void CallBinder(string mode)
    {
        var binderPath = Path.Combine(ScriptEngineDir, "Tools", "Binder.exe");
        var configPath = Path.GetFullPath(Path.Combine(ScriptEngineDir, "Tools", "config.json"));
        var toolsetPath = GetEnginePath(Path.Combine("Tools", "Roslyn"));

        var args = new List<string>() { configPath, toolsetPath };
        if (!string.IsNullOrEmpty(mode))
            args.Add(mode);


        var monoPath = GetEnginePath(Path.Combine("MonoBleedingEdge", "bin", Application.platform == RuntimePlatform.OSXEditor ? "mono" : "mono.exe")); 
        if (!File.Exists(monoPath))
        {
            UnityEngine.Debug.LogError("can not find mono!");
            return;
        }

        if (!File.Exists(binderPath))
        {
            UnityEngine.Debug.LogError("please install the Binder");
            return;
        }

        Process binder = new Process();
        binder.StartInfo.FileName = monoPath;

        string debugger = "--debug";
        //debugger += " --debugger-agent=transport=dt_socket,address=127.0.0.1:8089,server=y,suspend=y ";

        binder.StartInfo.Arguments = debugger + " --runtime=v4.0.30319 \"" + binderPath + "\" \"" + string.Join("\" \"", args.ToArray()) + "\"";
        binder.StartInfo.WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden;
        binder.StartInfo.RedirectStandardOutput = true;
        binder.StartInfo.RedirectStandardError = true;
        binder.StartInfo.UseShellExecute = false;
        binder.StartInfo.CreateNoWindow = true;
        binder.Start();

        while (!binder.StandardOutput.EndOfStream)
        {
            string line = binder.StandardOutput.ReadLine();
            UnityEngine.Debug.LogWarning(line);
        }

        binder.WaitForExit();

        if (binder.ExitCode != 0)
        {
            var errorInfo = "Binder.exe run error. \n" + binder.StandardError.ReadToEnd();
            UnityEngine.Debug.LogError(errorInfo);
            throw new Exception(errorInfo);
        }
    }


    public static void CopyManagedFile(string workDir,string managedPath)
    {
        CreateOrCleanDirectory(managedPath);

        foreach (string fi in Directory.GetFiles(workDir))
        {
            string fname = Path.GetFileName(fi);
            string targetfname = Path.Combine(managedPath, fname);
            File.Copy(fi, targetfname);
        }
    }

    public static string GetEnginePath(string path)
    {
        string dataPath;
        var editorAppPath = NiceWinPath(EditorApplication.applicationPath);

        if (Application.platform == RuntimePlatform.OSXEditor)
            dataPath = Path.Combine(editorAppPath, "Contents");
        else
            dataPath = Path.Combine(Path.GetDirectoryName(editorAppPath), "Data");

        return Path.Combine(dataPath, path);
    }

    static string NiceWinPath(string unityPath)
    {
        // IO functions do not like mixing of \ and / slashes, esp. for windows network paths (\\path)
        return Application.platform == RuntimePlatform.WindowsEditor ? unityPath.Replace("/", @"\") : unityPath;
    }

    static void CreateOrCleanDirectory(string dir)
    {
        if (Directory.Exists(dir))
            Directory.Delete(dir, true);
        Directory.CreateDirectory(dir);
    }



#region InsertBuildTask

    static MethodHooker stripHooker;
    static MethodHooker il2cppHooker;

    public static void RunIl2CppWithArguments(object obj, List<string> arguments, Action<System.Diagnostics.ProcessStartInfo> setupStartInfo, string workingDirectory)
    {
        if (il2cppHooker != null)
        {
            il2cppHooker.Dispose();
            il2cppHooker = null;
        }

        RunBinderBeforeIl2cpp(workingDirectory);

        // call the realy method
        if (obj != null)
            RunIl2CppWithArgumentsWrap(obj, arguments, setupStartInfo, workingDirectory);
    }

    //redirect to IL2CPPBuilder.RunIl2CppWithArguments
    public static void RunIl2CppWithArgumentsWrap(object obj, List<string> arguments, Action<System.Diagnostics.ProcessStartInfo> setupStartInfo, string workingDirectory)
    {
        throw new NotImplementedException();
    }


#if UNITY_2019_1_OR_NEWER
    public static void StripAssemblies(string managedAssemblyFolderPath, object unityLinkerPlatformProvider, object il2cppPlatformProvider, object rcr, ManagedStrippingLevel managedStrippingLevel)
#else
    public static void StripAssemblies(string managedAssemblyFolderPath, object platformProvider, object rcr, ManagedStrippingLevel managedStrippingLevel)
#endif
    {
        if (stripHooker != null)
        {
            stripHooker.Dispose();
            stripHooker = null;
        }
        RunBinderBeforeStrip(managedAssemblyFolderPath);

        // call the realy method
#if UNITY_2019_1_OR_NEWER
        StripAssembliesWrap(managedAssemblyFolderPath, unityLinkerPlatformProvider, il2cppPlatformProvider, rcr, managedStrippingLevel);
#else
        StripAssembliesWrap(managedAssemblyFolderPath, platformProvider, rcr, managedStrippingLevel);
#endif
    }

    //redirect to AssemblyStripper.StripAssemblies
#if UNITY_2019_1_OR_NEWER
    public static void StripAssembliesWrap(string managedAssemblyFolderPath, object unityLinkerPlatformProvider, object il2cppPlatformProvider, object rcr, ManagedStrippingLevel managedStrippingLevel)
#else
    public static void StripAssembliesWrap(string managedAssemblyFolderPath, object platformProvider, object rcr, ManagedStrippingLevel managedStrippingLevel)
#endif
    {
        throw new NotImplementedException();
    }
	
    private static void InsertBuildTask()
    {
        if (stripHooker == null)
        {
            var builderType = typeof(Editor).Assembly.GetType("UnityEditorInternal.AssemblyStripper");
            MethodBase orign = builderType.GetMethod("StripAssemblies", BindingFlags.Static | BindingFlags.NonPublic);
            MethodBase custom = typeof(PureScriptBuilder).GetMethod("StripAssemblies");
            MethodBase wrap2Orign = typeof(PureScriptBuilder).GetMethod("StripAssembliesWrap");
            stripHooker = new MethodHooker(orign, custom, wrap2Orign);
        }

        if (il2cppHooker == null)
        {
             var builderType = typeof(Editor).Assembly.GetType("UnityEditorInternal.IL2CPPBuilder");
             MethodBase orign = builderType.GetMethod("RunIl2CppWithArguments", BindingFlags.Instance | BindingFlags.NonPublic);
             MethodBase custom = typeof(PureScriptBuilder).GetMethod("RunIl2CppWithArguments");
             MethodBase wrap2Orign = typeof(PureScriptBuilder).GetMethod("RunIl2CppWithArgumentsWrap");
            il2cppHooker = new MethodHooker(orign, custom, wrap2Orign);
        }
    }
#endregion



#region internal

    private class MethodHooker : IDisposable
    {
        IntPtr orignPtr;
        IntPtr resumeData;
        int dataLength;

        public MethodHooker(MethodBase orign, MethodBase custom, MethodBase wrap2Orign)
        {
            RuntimeHelpers.PrepareMethod(orign.MethodHandle);
            RuntimeHelpers.PrepareMethod(custom.MethodHandle);
            RuntimeHelpers.PrepareMethod(wrap2Orign.MethodHandle);

            Debug.Log($"replace method:{orign.ToString()} -->{custom.ToString()}");
            RedirectMethod(wrap2Orign.MethodHandle.GetFunctionPointer(), orign.MethodHandle.GetFunctionPointer(),false);
            RedirectMethod(orign.MethodHandle.GetFunctionPointer(), custom.MethodHandle.GetFunctionPointer(),true);
        }

        public void Dispose()
        {
            Resume();
        }

        public unsafe void Resume()
        {
            if (resumeData == IntPtr.Zero || orignPtr == IntPtr.Zero || dataLength <= 0)
                return;

            Debug.Log($"resume method length:{dataLength.ToString()}");
            UnsafeUtility.MemCpy(orignPtr.ToPointer(), resumeData.ToPointer(), dataLength);
            Marshal.FreeHGlobal(resumeData);
            resumeData = IntPtr.Zero;
        }

        private unsafe void SaveResumeData(void* ptr,int length)
        {
            orignPtr = new IntPtr(ptr);
            dataLength = length;
            resumeData = Marshal.AllocHGlobal(dataLength);
            UnsafeUtility.MemCpy(resumeData.ToPointer(), ptr, dataLength);
        }

        private void RedirectMethod(IntPtr pBody, IntPtr pBorrowed,bool saveResume)
        {
            unsafe
            {
                var ptr = (byte*)pBody.ToPointer();
                var ptr2 = (byte*)pBorrowed.ToPointer();
                var ptrDiff = ptr2 - ptr - 5;
                if (ptrDiff < (long)0xFFFFFFFF && ptrDiff > (long)-0xFFFFFFFF)
                {
                    if(saveResume)
                        SaveResumeData(ptr,5);
                    // 32-bit relative jump, available on both 32 and 64 bit arch.
                    *ptr = 0xe9; // JMP
                    *((uint*)(ptr + 1)) = (uint)ptrDiff;
                }
                else
                {
                    if (Environment.Is64BitProcess)
                    {
                        if (saveResume)
                            SaveResumeData(ptr, 15);
                        // For 64bit arch and likely 64bit pointers, do:
                        // PUSH bits 0 - 32 of addr
                        // MOV [RSP+4] bits 32 - 64 of addr
                        // RET
                        var cursor = ptr;
                        *(cursor++) = 0x68; // PUSH
                        *((uint*)cursor) = (uint)ptr2;
                        cursor += 4;
                        *(cursor++) = 0xC7; // MOV [RSP+4]
                        *(cursor++) = 0x44;
                        *(cursor++) = 0x24;
                        *(cursor++) = 0x04;
                        *((uint*)cursor) = (uint)((ulong)ptr2 >> 32);
                        cursor += 4;
                        *(cursor++) = 0xc3; // RET
                    }
                    else
                    {
                        if (saveResume)
                            SaveResumeData(ptr, 6);
                        // For 32bit arch and 32bit pointers, do: PUSH addr, RET.
                        *ptr = 0x68;
                        *((uint*)(ptr + 1)) = (uint)ptr2;
                        *(ptr + 5) = 0xC3;
                    }
                }
            }
        }
    }
#endregion
}
