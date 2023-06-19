namespace FSharp.Data.NpgsqlGenerator.Tool

open Argu

[<RequireQualifiedAccess>]
type GenerateTypesArguments =
    | [<Mandatory; AltCommandLine("-c")>] Connection_String of connstring: string
    | [<AltCommandLine("-ns")>] Udf_Namespace of ``namespace``: string
    | [<AltCommandLine("-o")>] Output_Path of path: string

    interface IArgParserTemplate with
        member s.Usage =
            match s with
            | GenerateTypesArguments.Output_Path _ -> "Directory to place the generated files"
            | GenerateTypesArguments.Connection_String _ -> "Conn string to connect to dev postgres instance"
            | GenerateTypesArguments.Udf_Namespace _ -> "Target namespace for user defined types [Default=Db.Types]"

[<RequireQualifiedAccess>]
type GenerateAllArguments =
    | [<Mandatory; AltCommandLine("-c")>] Connection_String of connstring: string
    | [<AltCommandLine("-ns")>] Udf_Namespace of ``namespace``: string
    | [<AltCommandLine("-o")>] Output_Path of path: string
    | [<AltCommandLine("-tlc")>] Top_Level_Connections
    | [<AltCommandLine("-rrt")>] Record_Return_Types
    | [<Mandatory; Last; MainCommand>] Files of files: string list

    interface IArgParserTemplate with
        member s.Usage =
            match s with
            | GenerateAllArguments.Files _ -> "Input files to process"
            | GenerateAllArguments.Output_Path _ -> "Directory to place the generated files"
            | GenerateAllArguments.Connection_String _ -> "Conn string to connect to dev postgres instance"
            | GenerateAllArguments.Udf_Namespace _ -> "Namespace for user defined types [Default=Db.Types]"
            | GenerateAllArguments.Top_Level_Connections ->
                "Accept NpgsqlConnections at repository level instead of command level [default=false]"
            | GenerateAllArguments.Record_Return_Types _ ->
                "Generate classic, non anonymous F# record types as return types. Non anonymous result types help in better iterop with C# [default=true]"

[<RequireQualifiedAccess>]
type GenerateArguments =
    | [<CliPrefix(CliPrefix.None)>] All of ParseResults<GenerateAllArguments>
    | [<CliPrefix(CliPrefix.None)>] Types of ParseResults<GenerateTypesArguments>
    | [<CliPrefix(CliPrefix.None)>] Repositories of ParseResults<GenerateAllArguments>

    interface IArgParserTemplate with
        member s.Usage =
            match s with
            | GenerateArguments.All _ -> "Generate both user defined types and repositories"
            | GenerateArguments.Types _ -> "Generate user defined types only"
            | GenerateArguments.Repositories _ -> "Generate repositories only"


[<RequireQualifiedAccess>]
type CreateRepositoryArguments =
    | [<AltCommandLine("-n")>] Namespace of ``namespace``: string
    | [<AltCommandLine("-o")>] Output of path: string
    | [<Mandatory; Last; MainCommand>] Repository_Name of name: string

    interface IArgParserTemplate with
        member s.Usage =
            match s with
            | CreateRepositoryArguments.Namespace _ -> ".NET namespace of the repository"
            | CreateRepositoryArguments.Output _ -> "Directory for the generated output"
            | CreateRepositoryArguments.Repository_Name _ -> "Name of the repository"


[<RequireQualifiedAccess>]
type CreateCommandArguments =
    | [<AltCommandLine("-p")>] Prepared
    | [<AltCommandLine("-sr")>] Single_Row
    | [<Mandatory; AltCommandLine("-r")>] Repository of path: string
    | [<Mandatory; Last; MainCommand>] Name of name: string

    interface IArgParserTemplate with
        member s.Usage =
            match s with
            | CreateCommandArguments.Name _ -> "Name of the command"
            | CreateCommandArguments.Prepared _ -> "Whether the generator should generate a prepared statement"
            | CreateCommandArguments.Repository _ -> "Path of the repository descriptor to append to"
            | CreateCommandArguments.Single_Row _ -> "Execution of the command is supposed to return 0 or 1 rows"

[<RequireQualifiedAccess>]
type CLIArguments =
    | [<AltCommandLine("-v")>] Version
    | [<CliPrefix(CliPrefix.None)>] Generate of ParseResults<GenerateArguments>
    | [<CliPrefix(CliPrefix.None)>] Create_Command of ParseResults<CreateCommandArguments>
    | [<CliPrefix(CliPrefix.None)>] Create_Repository of ParseResults<CreateRepositoryArguments>

    interface IArgParserTemplate with
        member s.Usage =
            match s with
            | CLIArguments.Version _ -> "Print version and exit"
            | CLIArguments.Generate _ -> "Generate files"
            | CLIArguments.Create_Command _ -> "Add a new command template to a repository template file"
            | CLIArguments.Create_Repository _ -> "Create an empty repository template file"

module Parser =
    let parser =
        ArgumentParser.Create<CLIArguments>(
            programName = "npgsql-generator",
            helpTextMessage = "Generates database access logic based on provided SQL queries"
        )
