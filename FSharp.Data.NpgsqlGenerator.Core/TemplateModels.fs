namespace FSharp.Data.NpgsqlGenerator.Core.TemplateModels

open System.IO
open System.Reflection
open System.Threading.Tasks
open FSharp.Data.NpgsqlGenerator.Core.Inference
open Scriban
open Scriban.Parsing
open Scriban.Runtime


module internal String =
    let prefixWith (prefix: string) (input: string) = $"%s{prefix}%s{input}"

    let postfixWith (postfix: string) (input: string) = $"%s{input}%s{postfix}"

    let encapsulateIn (prefix: string) (input: string) (postfix: string) =
        input |> prefixWith prefix |> postfixWith postfix

module internal NameClashing =
    let private replacements =
        [ "type"
          "override"
          "new"
          "abstract"
          "as"
          "base"
          "bool"
          "break"
          "byte"
          "case"
          "catch"
          "char"
          "checked"
          "class"
          "const"
          "continue"
          "decimal"
          "default"
          "delegate"
          "do"
          "double"
          "else"
          "enum"
          "event"
          "explicit"
          "extern"
          "false"
          "finally"
          "fixed"
          "float"
          "for"
          "foreach"
          "goto"
          "if"
          "implicit"
          "in"
          "int"
          "interface"
          "internal"
          "is"
          "lock"
          "long"
          "namespace"
          "new"
          "null"
          "object"
          "operator"
          "out"
          "override"
          "params"
          "private"
          "protected"
          "public"
          "readonly"
          "ref"
          "return"
          "sbyte"
          "sealed"
          "short"
          "sizeof"
          "stackalloc"
          "static"
          "string"
          "struct"
          "switch"
          "this"
          "throw"
          "true"
          "try"
          "typeof"
          "uint"
          "ulong"
          "unchecked"
          "unsafe"
          "ushort"
          "using"
          "virtual"
          "void"
          "volatile"
          "while" ]
        |> Set.ofList
        |> Set.map (fun keyword -> keyword, $"``%s{keyword}``")
        |> Map.ofSeq

    let resolve (identifier: string) =
        replacements |> Map.tryFind identifier |> Option.defaultValue identifier

type EnumLabel = { Label: string; DbLabel: string }

type InferredEnum =
    { Oid: uint32
      Name: string
      ParameterName: string
      DbName: string
      Schema: string
      DbSchema: string
      SchemaOid: uint32
      Labels: EnumLabel[] }

module internal Namespace =
    let join (nspace: string) (ttype: string) = $"%s{nspace}.%s{ttype}"
    let joinMany (components: string list) = components |> String.concat "."

type public ResultColumnTemplate =
    { Ordinal: int
      Name: string
      DbName: string
      ClrType: string
      GetterName: string
      IsEnum: bool
      DeserializerName: string
      Nullable: bool }

type public CommandResultTemplate =
    { IsNonQuery: bool
      IsAnonymous: bool
      ReturnRecordTypeName: string
      ResultColumns: ResultColumnTemplate list }

type public CommandParameterTemplate =
    { Name: string
      SafeName: string
      ClrType: string
      IsEnum: bool
      SerializerName: string
      DataTypeName: string
      Nullable: bool }

type public CommandTemplate =
    { FunctionName: string
      Text: string
      Prepared: bool
      SingleRow: bool
      Parameters: CommandParameterTemplate list
      TopLevelConnections: bool
      Result: CommandResultTemplate
      PreparedInterfaceName: string
      PrepareFunctionName: string
      PrepareFunctionReturnType: string }

module internal CommandTemplate =
    let getReturnType (singleRow: bool) (resultTypeName: string) =
        if singleRow then
            String.encapsulateIn "Task<" resultTypeName "?>"
        else
            String.encapsulateIn "Task<IEnumerable<" resultTypeName ">>"

type public RepositoryTemplate =
    { InterfaceName: string
      Debug: bool
      TopLevelConnections: bool
      ImplementationClassName: string
      Namespace: string
      BaseNamespace: string
      Commands: CommandTemplate list }

module internal RepositoryTemplate =

    let interfaceName (repoName: string) =
        String.encapsulateIn "I" repoName "Repository"

    let implementationClassName (repoName: string) =
        repoName |> String.postfixWith "Repository"

    let interfaceNameFull (nspace: string) (interfaceName: string) = Namespace.join nspace interfaceName

type TemplateLoader(assemblyName: string) =
    member this.GetPath(templateName: string) : string =
        let langSegment = "FSharp"

        assemblyName
        |> String.postfixWith $".Templates.%s{langSegment}.%s{templateName}.sbn"

    member this.Load(path: string) : string =

        let assembly = Assembly.GetExecutingAssembly()

        use reader =
            let stream = assembly.GetManifestResourceStream(path)

            if stream = null then
                failwith $"Couldn't find template resource '%s{path}'"
            else
                new StreamReader(stream)


        reader.ReadToEnd()

    member this.LoadAsync(path: string) : ValueTask<string> =

        let assembly = Assembly.GetExecutingAssembly()

        use reader = new StreamReader(assembly.GetManifestResourceStream(path))

        ValueTask<string>(reader.ReadToEndAsync())

    interface ITemplateLoader with

        member this.GetPath(_context: TemplateContext, _span: SourceSpan, templateName: string) : string =
            this.GetPath templateName

        member this.Load(_context: TemplateContext, _span: SourceSpan, templatePath: string) : string =
            this.Load templatePath

        member this.LoadAsync(_context: TemplateContext, _span: SourceSpan, templatePath: string) : ValueTask<string> =
            this.LoadAsync templatePath
