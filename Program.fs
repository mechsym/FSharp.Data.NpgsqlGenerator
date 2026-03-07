open Fake.Core
open Fake.DotNet
open Fake.IO
open Fake.IO.FileSystemOperators
open Fake.Core.TargetOperators
open Fake.IO.Globbing.Operators


/// Directory that contains the produced .nupkg files
[<Literal>]
let PublishDirectory = "publish"

let releaseNotesFileName = "RELEASE_NOTES.md"

let releaseNotes = ReleaseNotes.load releaseNotesFileName

[<RequireQualifiedAccess>]
type Project =
    | Core
    | Tool

let projectDirs =
    [ Project.Core, "FSharp.Data.NpgsqlGenerator.Core"
      Project.Tool, "FSharp.Data.NpgsqlGenerator.Tool" ]
    |> Map.ofList

let projectFiles =
    [ Project.Core, "FSharp.Data.NpgsqlGenerator.Core.fsproj"
      Project.Tool, "FSharp.Data.NpgsqlGenerator.Tool.fsproj" ]
    |> Map.ofList

let projectPaths =
    [ Project.Core, Path.combine projectDirs.[Project.Core] projectFiles.[Project.Core]
      Project.Tool, Path.combine projectDirs.[Project.Tool] projectFiles.[Project.Tool] ]
    |> Map.ofList

let initTargets () =
  Target.create "AssemblyInfo" (fun _ ->
      let version = releaseNotes.NugetVersion

      AssemblyInfoFile.createFSharp
          (projectDirs.[Project.Tool] </> "AssemblyInfo.fs")
          [ AssemblyInfo.Title "FSharp.Data.NpgsqlGenerator.Tool"
            AssemblyInfo.Description "FSharp.Data.NpgsqlGenerator source generator CLI app"
            AssemblyInfo.Company "MechSym"
            AssemblyInfo.Product "FSharp.Data.NpgsqlGenerator source generator CLI"
            AssemblyInfo.Version version
            AssemblyInfo.FileVersion version ]

      AssemblyInfoFile.createFSharp
          (projectDirs.[Project.Core] </> "AssemblyInfo.fs")
          [ AssemblyInfo.Title "FSharp.Data.NpgsqlGenerator.Core"
            AssemblyInfo.Description "FSharp.Data.NpgsqlGenerator source generator core"
            AssemblyInfo.Company "MechSym"
            AssemblyInfo.Product "FSharp.Data.NpgsqlGenerator source generator core"
            AssemblyInfo.Version version
            AssemblyInfo.FileVersion version ])

  let addMsBuildOverrides (this: MSBuild.CliArguments) =
      { this with
          DisableInternalBinLog = true
          Properties = ("Version", releaseNotes.NugetVersion) :: this.Properties }

  Target.create "Clean" (fun _ ->
      projectDirs
      |> Map.values
      |> Seq.collect (fun path -> [ Path.combine path "bin"; Path.combine path "obj" ])
      |> Shell.cleanDirs

      [ PublishDirectory ] |> Shell.cleanDirs)

  Target.create "Build" (fun _ ->
      projectPaths
      |> Map.values
      |> Seq.iter (
          DotNet.build (fun parameters ->
              { parameters with
                  MSBuildParams = parameters.MSBuildParams |> addMsBuildOverrides })
      ))

  Target.create "Pack" (fun _ ->
      projectPaths
      |> Map.values
      |> Seq.iter (
          DotNet.pack (fun parameters ->
              { parameters with
                  OutputPath = Some PublishDirectory
                  MSBuildParams = parameters.MSBuildParams |> addMsBuildOverrides })
      ))


  Target.create "Push" (fun param ->
      let source =
          match param.Context.Arguments with
          | [] -> None
          | [ source ] -> Some source
          | _ -> failwith "Accepting a single push argument"

      !!(PublishDirectory </> "*.nupkg")
      |> Seq.iter (fun nupkg ->
          DotNet.nugetPush
              (fun parameters ->
                  { parameters with
                      PushParams =
                          { parameters.PushParams with
                              Source = source } })
              nupkg))

  Target.create "All" ignore

  "Clean" ==> "AssemblyInfo" ==> "Build" ==> "Pack" ==> "Push" ==> "All"

  Target.runOrDefaultWithArguments "All"


[<EntryPoint>]
let main argv =
    argv
    |> Array.toList
    |> Context.FakeExecutionContext.Create
        false
        // this really doesn't matter, being ignored by fake, it's just a mandatory parameter from the past
        "Build.fs"
    |> Context.RuntimeContext.Fake
    |> Context.setExecutionContext

    initTargets ()

    0
