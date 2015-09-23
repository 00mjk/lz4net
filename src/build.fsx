#r "packages/FAKE/tools/FakeLib.dll"

open Fake
open Fake.Testing
open StrongNamingHelper
open System.IO
open System.Text.RegularExpressions

let outDir = "./../out"
let testDir = outDir @@ "test"
let buildDir = outDir @@ "build"
let releaseDir = outDir @@ "release"
let snk = "LZ4.snk"

let testFile fn = (fileInfo fn).Exists


let updateVersionInfo productVersion (version: string) fileName = 
    let fixVersion commas = 
        match commas with | true -> version.Replace(".", ",") | _ -> version

    let productVersionRx = 
        [
            """(?<=^\s*\[assembly:\s*AssemblyVersion(Attribute)?\(")[0-9]+(\.([0-9]+|\*)){1,3}(?="\))""", false
            """(?<=^\s*PRODUCTVERSION\s+)[0-9]+(\,([0-9]+|\*)){1,3}(?=\s*$)""", true
            """(?<=^\s*VALUE\s+"ProductVersion",\s*")[0-9]+(\.([0-9]+|\*)){1,3}(?="\s*$)""", false 
        ]

    let fileVersionRx = 
        [
            """(?<=^\s*\[assembly:\s*AssemblyFileVersion(Attribute)?\(")[0-9]+(\.([0-9]+|\*)){1,3}(?="\))""", false
            """(?<=^\s*FILEVERSION\s+)[0-9]+(\,([0-9]+|\*)){1,3}(?=\s*$)""", true
            """(?<=^\s*VALUE\s+"FileVersion",\s*")[0-9]+(\.([0-9]+|\*)){1,3}(?="\s*$)""", false
        ]

    let allRx = 
        match productVersion with | false -> [] | _ -> productVersionRx
        |> List.append fileVersionRx
        |> List.map (fun (rx, c) -> (Regex(rx, RegexOptions.Multiline), c))

    let source = File.ReadAllText(fileName)
    let replace s (rx: Regex, c) = rx.Replace(s, fixVersion c)
    let target = allRx |> Seq.fold replace source

    if source <> target then
        trace (sprintf "Updating: %s" fileName)
        File.WriteAllText(fileName, target)

Target "KeyGen" (fun _ ->
    match testFile snk with
    | true -> ()
    | _ -> snk |> sprintf "-k %s" |> StrongName id
)

Target "Clean" (fun _ ->
    !! "**/bin" ++ "**/obj" |> CleanDirs
    "./../out" |> DeleteDir
)

Target "Build" (fun _ ->
    let build platform sln =
        sln
        |> MSBuildReleaseExt null [ ("Platform", platform) ] "Build"
        |> Log (sprintf "Build-%s-Output: " platform)

    !! "*.sln" |> build "x86"
    !! "*.sln" |> build "x64"
)

Target "Version" (fun _ ->
    !! "**/Properties/AssemblyInfo.cs"
    |> Seq.iter (updateVersionInfo true "1.0.6.93")
)

Target "Release" (fun _ ->
    [ "any"; "x86"; "x64" ]
    |> Seq.iter (fun dir ->
        let targetDir = releaseDir @@ dir
        targetDir |> CleanDir
        [ "LZ4.dll"; "LZ4pn.dll"; "LZ4ps.dll" ]
        |> Seq.map (fun fn -> "LZ4/bin/Release" @@ fn)
        |> Copy targetDir
    )

    [ "x86"; "x64" ] |> Seq.iter (fun platform ->
        let w32platform = match platform with | "x86" -> "win32" | _ -> platform
        [ "LZ4cc.dll"; "LZ4mm.dll" ]
        |> Seq.iter (fun fn ->
            let source = (sprintf "bin/%s/Release" w32platform) @@ fn
            let targetAny = releaseDir @@ "any" @@ (changeExt (sprintf ".%s.dll" platform) fn)
            let targetXxx = releaseDir @@ platform @@ fn
            source |> CopyFile targetAny
            source |> CopyFile targetXxx
        )
    )

    [ "portable"; "silverlight" ]
    |> Seq.iter (fun platform ->
        let sourceDir = sprintf "LZ4.%s/bin/Release" platform
        let targetDir = releaseDir @@ platform
        targetDir |> CleanDir
        !! (sourceDir @@ "*.dll") |> Copy targetDir
        let targetDll = targetDir @@ (sprintf "LZ4.%s.dll" platform)
        targetDll |> Rename (targetDir @@ "LZ4.dll")
    )

    releaseDir @@ "libz" |> CleanDir
    !! (releaseDir @@ "any" @@ "*.dll")
    |> CopyFiles (releaseDir @@ "libz")

    let fullSnk = (fileInfo "LZ4.snk").FullName
    let libzApp = "packages/LibZ.Bootstrap/tools/libz.exe"
    let libzArgs = sprintf "inject-dll -a LZ4.dll -i *.dll -e LZ4.dll --move -k \"%s\"" fullSnk
    { defaultParams with
        Program = libzApp;
        WorkingDirectory = releaseDir @@ "libz";
        CommandLine = libzArgs }
    |> shellExec |> ignore
)

"KeyGen" ==> "Build"
"Clean" ==> "Release"
"Build" ==> "Release"

RunTargetOrDefault "Release"
