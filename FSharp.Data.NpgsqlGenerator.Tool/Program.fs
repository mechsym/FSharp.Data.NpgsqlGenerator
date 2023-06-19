open System
open System.IO
open System.Text
open FSharp.Data.NpgsqlGenerator.Core
open FSharp.Data.NpgsqlGenerator.Core.Inference
open FSharp.Data.NpgsqlGenerator.Tool
open FsToolkit.ErrorHandling

open Thoth.Json.Net


type InferredCommandMeta =
    { Name: CommandName
      IsPrepared: bool
      SingleRow: bool }

module InferredCommandMeta =
    let toJson (this: InferredCommandMeta) =
        Encode.object
            [ "name", this.Name |> CommandName.get |> Encode.string
              "isPrepared", Encode.bool this.IsPrepared
              "singleRow", Encode.bool this.SingleRow ]

    let fromJson =
        Decode.object (fun getter ->
            { InferredCommandMeta.Name = getter.Required.Field "name" Decode.string |> CommandName.from
              IsPrepared = getter.Optional.Field "isPrepared" Decode.bool |> Option.defaultValue false

              SingleRow = getter.Optional.Field "singleRow" Decode.bool |> Option.defaultValue false })

type ReadingState =

    { RemainingLines: string list
      LastLineNum: int
      InferredCommands: InferredCommand list }

module ReadingState =
    let init (input: string list) =
        { RemainingLines = input
          LastLineNum = 0
          InferredCommands = [] }

    let consumeLine (this: ReadingState) =
        { this with
            LastLineNum = this.LastLineNum + 1
            RemainingLines = this.RemainingLines |> List.tail }

    let addCommand (command: InferredCommand) (this: ReadingState) =
        { this with
            InferredCommands = command :: this.InferredCommands }

type SeekingMetaState = { ReadingState: ReadingState }

type MetaReadingState =
    { ReadingState: ReadingState
      Buffer: StringBuilder
      FirstLine: int }

type SeekingCommandState =
    { ReadingState: ReadingState
      CommandMeta: InferredCommandMeta }

type ReadingCommandState =
    { ReadingState: ReadingState
      Buffer: StringBuilder
      CommandMeta: InferredCommandMeta }

[<RequireQualifiedAccess>]
type ParseState =
    | SeekingMeta of SeekingMetaState
    | ReadingMeta of MetaReadingState
    | SeekingCommand of SeekingCommandState
    | ReadingCommand of ReadingCommandState

[<RequireQualifiedAccess>]
type ParsingError =
    | JsonSerialization of startingLine: int * originalError: string
    | UnexpectedEofWhileReadingMeta of startingLine: int * lastLine: int
    | UnexpectedEofWhileSeekingCommand of command: CommandName
    | UnexpectedEofWhileReadingCommand of command: CommandName

module ParsingError =
    let toString: ParsingError -> string =
        function
        | ParsingError.JsonSerialization(startingLine, message) ->
            let line = startingLine + 1
            $"failed to parse command metadata json beginning at line #%d{line}: '%s{message}'"
        | ParsingError.UnexpectedEofWhileReadingMeta(startingLine, lastLine) ->
            let line = startingLine + 1
            $"failed to parse command metadata json beginning at line #%d{line}: unexpected end of file encountered after line #%d{lastLine}. Expecting: '*/'"
        | ParsingError.UnexpectedEofWhileSeekingCommand commandName ->
            let name = commandName |> CommandName.get
            $"failed to find command text for command '%s{name}': unexpected end of file. Expecting: '<sql text>;'"
        | ParsingError.UnexpectedEofWhileReadingCommand commandName ->
            let name = commandName |> CommandName.get
            $"failed to finish reading command text for command '%s{name}': unexpected end of file. Expecting: '<sql text>;'"

type FileEnrichedParsingError =
    { Error: ParsingError
      Repository: RepositoryName }

module FileEnrichedParsingError =
    let toString (this: FileEnrichedParsingError) : string =
        $"%s{this.Repository |> RepositoryName.get} - %s{this.Error |> ParsingError.toString}"

    let from (repository: RepositoryName) (error: ParsingError) : FileEnrichedParsingError =
        { FileEnrichedParsingError.Error = error
          Repository = repository }

[<RequireQualifiedAccess>]
type GenerationError =
    | ParsingError of FileEnrichedParsingError
    | IllegalFileName of fileName: string

module GenerationError =
    let toString: GenerationError -> string =
        function
        | GenerationError.ParsingError err -> err |> FileEnrichedParsingError.toString
        | GenerationError.IllegalFileName fileName ->
            $"'%s{fileName}' is not a valid name for a repository. it must contain at least two components [<namespace>, <repository name>] separated by a '.'"

let parseRepositoryFile (content: string list) : Result<InferredCommand list, ParsingError> =

    let rec loop (state: ParseState) : Result<InferredCommand list, ParsingError> =
        match state with
        | ParseState.SeekingMeta state ->
            match state.ReadingState.RemainingLines with
            | line :: _ ->

                let readingState2 = state.ReadingState |> ReadingState.consumeLine

                let isCommentStartToken = line.Trim() = "/*"

                if isCommentStartToken then
                    loop (
                        ParseState.ReadingMeta
                            { MetaReadingState.Buffer = StringBuilder()
                              ReadingState = readingState2
                              FirstLine = readingState2.LastLineNum }
                    )
                else
                    loop (
                        ParseState.SeekingMeta(
                            { state with
                                ReadingState = readingState2 }
                        )
                    )
            | [] -> Ok state.ReadingState.InferredCommands
        | ParseState.ReadingMeta state ->
            match state.ReadingState.RemainingLines with
            | line :: _ ->
                let readingState2 = state.ReadingState |> ReadingState.consumeLine

                let isCommentEndToken = line.Trim() = "*/"

                if isCommentEndToken then
                    state.Buffer.ToString()
                    |> Decode.fromString InferredCommandMeta.fromJson
                    |> Result.mapError (fun serializerError ->
                        ParsingError.JsonSerialization(state.FirstLine, serializerError))
                    |> Result.bind (fun parsedMeta ->
                        loop (
                            ParseState.SeekingCommand
                                { SeekingCommandState.ReadingState = readingState2
                                  CommandMeta = parsedMeta }
                        ))
                else
                    state.Buffer.AppendLine line |> ignore

                    loop (
                        ParseState.ReadingMeta
                            { ReadingState = readingState2
                              MetaReadingState.Buffer = state.Buffer
                              FirstLine = state.FirstLine }
                    )

            | [] -> Error(ParsingError.UnexpectedEofWhileReadingMeta(state.FirstLine, state.ReadingState.LastLineNum))

        | ParseState.SeekingCommand state ->
            match state.ReadingState.RemainingLines with
            | line :: _ ->
                if line.Trim() = "" then
                    let readingState2 = state.ReadingState |> ReadingState.consumeLine

                    loop (
                        ParseState.SeekingCommand
                            { state with
                                ReadingState = readingState2 }
                    )
                else
                    loop (
                        ParseState.ReadingCommand
                            // NB: command reading step will consume the first command line
                            { ReadingCommandState.ReadingState = state.ReadingState
                              Buffer = StringBuilder()
                              CommandMeta = state.CommandMeta }
                    )
            | [] -> Error(ParsingError.UnexpectedEofWhileSeekingCommand state.CommandMeta.Name)
        | ParseState.ReadingCommand state ->
            match state.ReadingState.RemainingLines with
            | line :: _ ->
                let trimmedLine = line.Trim()

                let readingState2 = state.ReadingState |> ReadingState.consumeLine

                state.Buffer.AppendLine line |> ignore

                if trimmedLine.EndsWith ";" then
                    loop (
                        ParseState.SeekingMeta
                            { SeekingMetaState.ReadingState =
                                readingState2
                                |> ReadingState.addCommand
                                    { InferredCommand.Name = state.CommandMeta.Name
                                      Prepared = state.CommandMeta.IsPrepared
                                      SingleRow = state.CommandMeta.SingleRow
                                      Text = state.Buffer.ToString().Trim() |> CommandText.from } }
                    )
                else
                    loop (
                        ParseState.ReadingCommand
                            { ReadingCommandState.ReadingState = readingState2
                              Buffer = state.Buffer
                              CommandMeta = state.CommandMeta }
                    )
            | [] -> Error(ParsingError.UnexpectedEofWhileReadingCommand state.CommandMeta.Name)

    loop (ParseState.SeekingMeta { SeekingMetaState.ReadingState = ReadingState.init content })

type GeneratedRepository =
    { OriginalFile: string
      GeneratedContent: string }

type GenerationResult =
    { Enums: string
      Repositories: GeneratedRepository list }

let generate
    (connString: string)
    (udfNamespace: Namespace)
    (topLevelConnections: bool)
    (anonymousReturnTypes: bool)
    (files: string list)
    : Result<GenerationResult, GenerationError list> =
    let config =
        { InferredConfiguration.ConnString = connString
          UdfNamespace = udfNamespace
          TopLevelConnections = topLevelConnections
          IsDebug = false
          AnonymousReturnTypes = anonymousReturnTypes }

    let enums, enumsFile = EnumGenerator.generate config

    files
    |> List.traverseResultA (fun file ->
        let fileWithoutExtension = Path.GetFileNameWithoutExtension file

        let split = fileWithoutExtension.Split([| '.' |]) |> Array.toList |> List.rev

        match split with
        | [] -> Error(GenerationError.IllegalFileName fileWithoutExtension)
        | [ repositoryName ] -> Ok(repositoryName, "Generated")
        | repositoryName :: namespaceComponents ->
            Ok(repositoryName, namespaceComponents |> List.rev |> String.concat ".")
        |> Result.map (fun (repo, nspace) -> RepositoryName.from repo, Namespace.from nspace)
        |> Result.bind (fun (repositoryName, namespaceName) ->
            File.ReadAllLines file
            |> Array.toList
            |> parseRepositoryFile
            |> Result.mapError (FileEnrichedParsingError.from repositoryName >> GenerationError.ParsingError)
            |> Result.map List.rev
            |> Result.map (fun commands ->
                { GeneratedRepository.OriginalFile = file
                  GeneratedContent =
                    RepositoryGenerator.generate (
                        { InferredRepository.Name = repositoryName
                          Namespace = namespaceName
                          Commands = commands },
                        GenerationContext.from enums config
                    ) })))
    |> Result.map (fun repositoryFiles ->
        { GenerationResult.Enums = enumsFile
          Repositories = repositoryFiles })

let getOutDir (parameter: string option) =
    parameter |> Option.defaultValue Environment.CurrentDirectory

let getUdfNamespace (parameter: string option) : Namespace =
    parameter |> Option.defaultValue "Db.Types" |> Namespace.from

let getOutputFileName (outDir: string) (file: string) =

    let extension = "g.fs"

    let file = file |> Path.GetFileNameWithoutExtension

    Path.Join(outDir, $"%s{file}.%s{extension}")

[<EntryPoint>]
let main args =
    try
        let looselyParsed =
            Parser.parser.ParseCommandLine(inputs = args, raiseOnUsage = true, ignoreMissing = true)

        if looselyParsed.Contains CLIArguments.Version then
            printfn "npgsql-generator version %s" AssemblyVersionInformation.AssemblyVersion
            0
        else
            let strictlyParsed =
                Parser.parser.ParseCommandLine(inputs = args, raiseOnUsage = true)

            match strictlyParsed.GetSubCommand() with
            | CLIArguments.Version _ -> failwith "This has been handled by the previous parsing"
            | CLIArguments.Create_Repository createRepositoryArgs ->
                let ``namespace`` =
                    createRepositoryArgs.TryGetResult CreateRepositoryArguments.Namespace
                    |> Option.defaultValue "Global"

                let output =
                    createRepositoryArgs.TryGetResult CreateRepositoryArguments.Output
                    |> Option.defaultValue Environment.CurrentDirectory

                let name = createRepositoryArgs.GetResult CreateRepositoryArguments.Repository_Name

                let fileName = [ ``namespace``; name; "sql" ] |> String.concat "."

                Directory.CreateDirectory(output) |> ignore

                let fullPath = Path.Join(output, fileName)

                use fs = FileInfo(fullPath).Create()

                0

            | CLIArguments.Create_Command createCommandArgs ->

                let name =
                    createCommandArgs.GetResult CreateCommandArguments.Name |> CommandName.from

                let prepared = createCommandArgs.Contains CreateCommandArguments.Prepared

                let file = createCommandArgs.GetResult CreateCommandArguments.Repository

                let singleRow = createCommandArgs.Contains CreateCommandArguments.Single_Row

                let meta =
                    { InferredCommandMeta.Name = name
                      SingleRow = singleRow
                      IsPrepared = prepared }
                    |> InferredCommandMeta.toJson
                    |> Encode.toString 2

                use writer = File.AppendText(file)
                writer.WriteLine "/*"
                writer.Write meta
                writer.WriteLine ""
                writer.WriteLine "*/"
                writer.WriteLine "<INSERT YOUR SQL HERE, TERMINATED BY ';'>;"
                0

            | CLIArguments.Generate generateArgs ->

                match generateArgs.GetSubCommand() with
                | GenerateArguments.All generateArgs ->

                    let outDir = generateArgs.TryGetResult GenerateAllArguments.Output_Path |> getOutDir

                    let connString = generateArgs.GetResult GenerateAllArguments.Connection_String

                    let connString = connString.Trim('"')

                    let udfNamespace =
                        generateArgs.TryGetResult GenerateAllArguments.Udf_Namespace |> getUdfNamespace

                    let files = generateArgs.GetResult GenerateAllArguments.Files

                    let topLevelConnections =
                        generateArgs.Contains GenerateAllArguments.Top_Level_Connections

                    let anonymousReturnTypes =
                        generateArgs.Contains GenerateAllArguments.Record_Return_Types |> not

                    let result =
                        generate connString udfNamespace topLevelConnections anonymousReturnTypes files

                    match result with
                    | Ok result2 ->

                        Directory.CreateDirectory outDir |> ignore

                        do
                            let udfFile =
                                let rawNamespace = Namespace.get udfNamespace
                                getOutputFileName outDir rawNamespace

                            File.WriteAllText(udfFile, result2.Enums)

                        for repository in result2.Repositories do
                            let repositoryFile = getOutputFileName outDir repository.OriginalFile

                            File.WriteAllText(repositoryFile, repository.GeneratedContent)

                        0
                    | Error errors ->
                        for error in errors do
                            printfn "%s" (error |> GenerationError.toString)

                        1

                | GenerateArguments.Types generateArgs ->

                    let outDir =
                        generateArgs.TryGetResult GenerateTypesArguments.Output_Path |> getOutDir

                    let connString = generateArgs.GetResult GenerateTypesArguments.Connection_String

                    let connString = connString.Trim('"')

                    let udfNamespace =
                        generateArgs.TryGetResult GenerateTypesArguments.Udf_Namespace
                        |> getUdfNamespace


                    let result =
                        // It doesn't really matter in this case, as we are not generating data access functions right now
                        // so the question is not applicable here.
                        let topLevelConnections = false
                        let anonymousReturnTypes = false
                        generate connString udfNamespace topLevelConnections anonymousReturnTypes []

                    match result with
                    | Ok result2 ->

                        Directory.CreateDirectory outDir |> ignore

                        do
                            let udfFile =
                                let rawNamespace = Namespace.get udfNamespace
                                getOutputFileName outDir rawNamespace

                            File.WriteAllText(udfFile, result2.Enums)


                        for repository in result2.Repositories do
                            let repositoryFile = getOutputFileName outDir repository.OriginalFile

                            File.WriteAllText(repositoryFile, repository.GeneratedContent)

                        0
                    | Error errors ->

                        for error in errors do
                            printfn "%s" (error |> GenerationError.toString)

                        1

                | GenerateArguments.Repositories generateArgs ->

                    let outDir = generateArgs.TryGetResult GenerateAllArguments.Output_Path |> getOutDir

                    let connString = generateArgs.GetResult GenerateAllArguments.Connection_String

                    let connString = connString.Trim('"')

                    let udfNamespace =
                        generateArgs.TryGetResult GenerateAllArguments.Udf_Namespace |> getUdfNamespace

                    let files = generateArgs.GetResult GenerateAllArguments.Files

                    let topLevelConnections =
                        generateArgs.Contains GenerateAllArguments.Top_Level_Connections

                    let anonymousReturnTypes =
                        generateArgs.Contains GenerateAllArguments.Record_Return_Types |> not

                    let result =
                        generate connString udfNamespace topLevelConnections anonymousReturnTypes files

                    match result with
                    | Ok result2 ->
                        Directory.CreateDirectory outDir |> ignore

                        for repository in result2.Repositories do
                            let repositoryFile = getOutputFileName outDir repository.OriginalFile

                            File.WriteAllText(repositoryFile, repository.GeneratedContent)

                        0
                    | Error errors ->

                        for error in errors do
                            printfn "%s" (error |> GenerationError.toString)

                        1
    with
    | :? Argu.ArguParseException as parseEx ->
        if parseEx.ErrorCode = Argu.ErrorCode.HelpText then
            printfn "%s" parseEx.Message
            0
        else
            printfn "%s" parseEx.Message
            1
    | e ->
        printfn "%s" e.Message
        printfn "%s" e.StackTrace
        1
