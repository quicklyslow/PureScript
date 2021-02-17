﻿using PureScript;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;

public class Lancher : MonoBehaviour
{

    UnityEngine.Video.VideoPlayer aa;
    static string BundleDir;

    // Start is called before the first frame update
    void Start()
    {
        BundleDir = @"F:\Project\UnityBind\PureScript\DemoProject"; // Application.dataPath.Replace("Assets", "Managed");
    }

    void OnGUI()
    {
        if(GUI.Button(new Rect(10,20,50,30),"Load Mono"))
        {
            var ptr = UnityBind.BindFunc();
            ScriptEngine.SetFuncPointer(ptr);
            ScriptEngine.SetupMono(BundleDir, "MonoTest.exe");
        }
        if (GUI.Button(new Rect(10, 60, 50, 30), "Close Mono"))
        {
            ScriptEngine.CloseMono();
        }
    }
}
