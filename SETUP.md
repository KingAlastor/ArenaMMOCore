# ArenaMMOCore Setup Guide

This document contains step-by-step instructions to configure and run the ArenaMMOCore project. This architecture utilizes a headless, zero-allocation, pure-math **.NET Core Server running at 30Hz**, a shared data-packing **C# Shared Library**, and a high-performance **Unity DOTS/ECS Client** optimized for low-end hardware.

---

## 📋 Prerequisites

Ensure the following environments are installed on your workstation before proceeding:

1. **.NET SDK 8.0, 9.0, or 10.0** (Required for headless server compilation)
2. **Unity Editor 2022.3 LTS or 2023.2+** 
3. **IDE**: Rider, Visual Studio Code (with C# Dev Kit), or Visual Studio 2022+

---

## 📂 Project Architecture

Your workspace must be organized into these three core root directories:

```text
ArenaMMOCore/
├── SharedLibrary/    # Pure C# value types, MemoryPack serializers, and game math
├── GameServer/       # Standalone .NET Console app (Headless 30Hz physics-free loop)
└── GameClient/       # Unity Project Root containing DOTS configurations
```

---

## ⚙️ Step 1: Shared Library & Automated Copy Pipeline

The `SharedLibrary` handles networking binary blitting using **MemoryPack** and transport primitives via **LiteNetLib**. It compiles to a `.dll` and automatically deploys itself into the Unity client project.

1. Open your terminal and navigate to the `SharedLibrary` folder:
   ```bash
   cd SharedLibrary
   ```
2. Ensure your `SharedLibrary.csproj` matches this configuration to automate dependencies copy mapping:
   ```xml
   <Project Sdk="Microsoft.NET.Sdk">
     <PropertyGroup>
       <TargetFramework>netstandard2.1</TargetFramework>
       <Nullable>enable</Nullable>
       <CopyLocalLockFileAssemblies>true</CopyLocalLockFileAssemblies>
     </PropertyGroup>

     <ItemGroup>
       <PackageReference Include="LiteNetLib" Version="2.1.4" />
       <PackageReference Include="MemoryPack" Version="1.21.4" />
     </ItemGroup>

     <Target Name="CopyToUnity" AfterTargets="Build">
       <PropertyGroup>
         <UnityPluginsDir>$(ProjectDir)..\GameClient\Assets\Plugins\</UnityPluginsDir>
       </PropertyGroup>
       <MakeDir Directories="$(UnityPluginsDir)" Condition="!Exists('$(UnityPluginsDir)')" />
       <Copy SourceFiles="$(TargetDir)$(TargetName).dll" DestinationFolder="$(UnityPluginsDir)" ContinueOnError="false" />
       <Copy SourceFiles="$(TargetDir)$(TargetName).pdb" DestinationFolder="$(UnityPluginsDir)" ContinueOnError="false" />
       <Copy SourceFiles="$(TargetDir)LiteNetLib.dll" DestinationFolder="$(UnityPluginsDir)" ContinueOnError="false" />
     </Target>
   </Project>
   ```
3. Restore packages and run your first build execution:
   ```bash
   dotnet restore
   dotnet build
   ```
   *Verify that `SharedLibrary.dll`, `SharedLibrary.pdb`, and `LiteNetLib.dll` have been generated and pushed automatically into your `GameClient/Assets/Plugins/` directory.*

---

## 🖥️ Step 2: Game Server Setup

The server runs as a standalone C# process completely independent of Unity, bypassing the managed heap entirely to guarantee high concurrency performance.

1. Navigate to your server subdirectory:
   ```bash
   cd ../GameServer
   ```
2. Verify dependencies by checking `GameServer.csproj` (or add them via terminal):
   ```bash
   dotnet add package LiteNetLib
   dotnet add package MemoryPack
   ```
3. Compile the headless environment to check layout integrity:
   ```bash
   dotnet build
   ```
4. Start your 30Hz network socket loop:
   ```bash
   dotnet run
   ```

---

## 🎮 Step 3: Unity Game Client Setup (DOTS / ECS)

To prevent Unity from locking up or freezing your hardware during initial asset integration, **always configure your dependency package manifests before booting the editor layout.**

### 1. Manual Safe Manifest Installation (Do this while Unity is CLOSED)
1. Navigate to your project path: `ArenaMMOCore/GameClient/Packages/`
2. Open the file named `manifest.json` using a standard text editor.
3. Locate the `"dependencies": {` section, and carefully inject the official Unity Entities package alongside your existing entries:
   ```json
   "dependencies": {
     "com.unity.entities": "1.0.16",
     "com.unity.properties": "1.3.1",
     "com.unity.serialization": "3.1.2"
   }
   ```
   *(Note: Ensure your exact version identifier matches your Unity Editor installation targets. Leaving out a string completely or mismatching version ranges can cause safe-mode loops).*
4. Save and close `manifest.json`.

### 2. Initial Boot and Cache Refresh
1. If your editor previously crashed or locked up, delete the localized compilation cache folders to prevent corrupted artifact tracking:
   ```bash
   cd ../GameClient
   rm -rf Library/ Temp/ obj/
   ```
2. Open the **Unity Hub** and add/launch the `GameClient` project.
3. Allow Unity several minutes to index the clean database setup. It will automatically download the required ECS runtime systems from the Unity Registry safely.

### 3. Injecting MemoryPack into Unity
Because our shared serialization files depend on **MemoryPack**, Unity requires the core MemoryPack attribute packages to translate the generated structures.
1. Inside the Unity Editor, select **Window ──► Package Manager**.
2. Click the **`+`** icon in the upper left corner and select **Add package from git URL...**
3. Paste this official registry tracking line:
   ```text
   https://github.com
   ```
4. Click **Add** and let Unity complete its compile pass.

### 4. Project Layout Setup
Ensure your game code files match this explicit tree inside your `Assets/` layout directory:
```text
Assets/
├── Plugins/
│   ├── LiteNetLib.dll       (Auto-Copied)
│   ├── SharedLibrary.dll    (Auto-Copied)
│   └── SharedLibrary.pdb    (Auto-Copied)
└── Netcode/
    ├── PlayerComponents.cs
    ├── PlayerAuthoring.cs
    ├── NetworkClientManager.cs
    └── NetworkMovementSystem.cs
```

---

## 🚀 Verifying the Complete Network Loop

1. Run the headless loop in an isolated terminal node:
   ```bash
   cd ArenaMMOCore/GameServer
   dotnet run
   ```
2. Open your Unity project scene workspace.
3. Create an empty GameObject named `NetworkManager` and attach the `NetworkClientManager` script components.
4. Create a 3D Cube primitive, attach the `PlayerAuthoring` component script, right-click the object, and transform it into a **SubScene**.
5. Enter **Play Mode** in Unity, click inside the Game View, and press **W, A, S, or D** to observe zero-allocation, server-authoritative transformation updates passing across your loop!
