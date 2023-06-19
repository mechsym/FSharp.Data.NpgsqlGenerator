namespace FSharp.Data.NpgsqlGenerator.Core

open System
open System.Data
open System.Reflection
open FSharp.Data.NpgsqlGenerator.Core.Generator.Schema
open FSharp.Data.NpgsqlGenerator.Core.Inference
open FSharp.Data.NpgsqlGenerator.Core.TemplateModels
open Microsoft.FSharp.Collections
open Npgsql
open Scriban
open Humanizer
open Scriban.Runtime

type GenerationContext =
    { EnumLookupByOid: Map<uint32, InferredEnum>
      EnumLookupByDbName: Map<string, InferredEnum>
      Config: InferredConfiguration }

module GenerationContext =
    let from (enums: InferredEnum[]) (config: InferredConfiguration) =
        let enumLookupByOid = enums |> Array.map (fun enum -> enum.Oid, enum) |> Map.ofArray

        let enumLookupByDbName =
            enums
            |> Array.map (fun enum -> $"%s{enum.DbSchema}.%s{enum.DbName}", enum)
            |> Map.ofArray

        { EnumLookupByOid = enumLookupByOid
          EnumLookupByDbName = enumLookupByDbName
          GenerationContext.Config = config }

    let withConfig (config: InferredConfiguration) (this: GenerationContext) = { this with Config = config }

module RepositoryGenerator =
    let private generateParameterTemplates
        (context: GenerationContext)
        (sqlCommand: NpgsqlCommand)
        : CommandParameterTemplate list =

        [ for parameter in sqlCommand.Parameters ->
              let nullable = parameter.IsNullable

              let safeName = NameClashing.resolve parameter.ParameterName

              match context.EnumLookupByDbName |> Map.tryFind parameter.DataTypeName with
              | Some enum ->
                  let enumNamespace =
                      let rawNamespace = context.Config.UdfNamespace |> Namespace.get
                      Namespace.join rawNamespace enum.Schema

                  let fullName = Namespace.join enumNamespace enum.Name

                  let finalClrType =

                      if nullable then $"%s{fullName} option" else fullName

                  let serializerName = Namespace.joinMany [ fullName; "toString" ]


                  { CommandParameterTemplate.Name = parameter.ParameterName
                    SafeName = safeName
                    ClrType = finalClrType
                    Nullable = nullable
                    DataTypeName = parameter.DataTypeName
                    IsEnum = true
                    SerializerName = serializerName }
              | None ->
                  let dataType = DataType.from nullable parameter.PostgresType

                  { CommandParameterTemplate.Name = parameter.ParameterName
                    ClrType = dataType |> DataType.finalClrType
                    Nullable = nullable
                    SafeName = safeName
                    SerializerName = null
                    IsEnum = false
                    DataTypeName = parameter.DataTypeName } ]

    let private generateResultColumnTemplates
        (context: GenerationContext)
        (sqlCommand: NpgsqlCommand)
        : ResultColumnTemplate list =

        use result =
            sqlCommand.ExecuteReader(CommandBehavior.KeyInfo ||| CommandBehavior.SchemaOnly)

        let resultColumns = result.GetColumnSchema()

        [ for column in resultColumns ->
              let maybeEnum = context.EnumLookupByOid |> Map.tryFind column.TypeOID

              let nullable = column.AllowDBNull.GetValueOrDefault(true)

              match maybeEnum with
              | Some enum ->
                  let enumNamespace =
                      let rawNamespace = context.Config.UdfNamespace |> Namespace.get
                      Namespace.join rawNamespace enum.Schema

                  let fullName = Namespace.join enumNamespace enum.Name

                  let finalClrType = if nullable then $"%s{fullName} option" else fullName

                  let deserializerName = Namespace.joinMany [ fullName; "fromString" ]

                  { Ordinal = column.ColumnOrdinal.Value
                    Name = column.ColumnName.Pascalize()
                    DbName = column.ColumnName
                    GetterName = Builtins.getters["enum"]
                    IsEnum = true
                    DeserializerName = deserializerName
                    ResultColumnTemplate.Nullable = column.AllowDBNull.GetValueOrDefault(true)
                    ClrType = finalClrType }
              | None ->

                  let dataType = DataType.from nullable column.PostgresType

                  { Ordinal = column.ColumnOrdinal.Value
                    Name = column.ColumnName.Pascalize()
                    DbName = column.ColumnName
                    GetterName =
                      Builtins.getters
                      |> Map.tryFind dataType.Name
                      |> Option.defaultValue "throw new Exception(\"getter was not found\")"
                    IsEnum = false
                    DeserializerName = null
                    ResultColumnTemplate.Nullable = column.AllowDBNull.GetValueOrDefault(true)
                    ClrType = dataType |> DataType.finalClrType }

          ]

    let private generateCommandTemplate
        (context: GenerationContext)
        (conn: NpgsqlConnection)
        (command: InferredCommand)
        : CommandTemplate =
        use sqlCommand = new NpgsqlCommand(command.Text |> CommandText.get, conn)

        NpgsqlCommandBuilder.DeriveParameters sqlCommand

        for param in sqlCommand.Parameters do
            param.Value <- DBNull.Value

        sqlCommand.Prepare()

        let parameters = sqlCommand |> generateParameterTemplates context

        let result =
            let resultColumns = sqlCommand |> generateResultColumnTemplates context

            let returnRecordTypeName = $"%s{command.Name |> CommandName.get}Result"

            { CommandResultTemplate.IsNonQuery = resultColumns |> List.isEmpty
              ResultColumns = resultColumns
              IsAnonymous = context.Config.AnonymousReturnTypes
              ReturnRecordTypeName = returnRecordTypeName }

        let preparedInterfaceName = $"IPrepared%s{command.Name |> CommandName.get}"

        let functionName = command.Name |> CommandName.get

        let prepareFunctionName = functionName |> String.prefixWith "Prepare"

        let prepareFunctionReturnType =
            let getPrepareFunctionReturnType typeName =
                String.encapsulateIn "Task<" typeName ">"

            getPrepareFunctionReturnType preparedInterfaceName

        let topLevelConnection = context.Config.TopLevelConnections

        { CommandTemplate.Text = command.Text |> CommandText.get
          FunctionName = functionName
          TopLevelConnections = topLevelConnection
          Prepared = command.Prepared
          SingleRow = command.SingleRow
          Result = result
          Parameters = parameters
          PreparedInterfaceName = preparedInterfaceName
          PrepareFunctionName = prepareFunctionName
          PrepareFunctionReturnType = prepareFunctionReturnType }

    let generate (repository: InferredRepository, context: GenerationContext) =
        use conn = new NpgsqlConnection(context.Config.ConnString)

        conn.Open()

        let templateData =
            let repositoryInterfaceName =
                repository.Name |> RepositoryName.get |> RepositoryTemplate.interfaceName

            let baseNamespace = repository.Name |> RepositoryName.get

            let nspaceName =
                let rawNamespace = repository.Namespace |> Namespace.get
                Namespace.join rawNamespace "Generated"

            let repositoryImplementationName =
                repository.Name
                |> RepositoryName.get
                |> RepositoryTemplate.implementationClassName

            { RepositoryTemplate.InterfaceName = repositoryInterfaceName
              ImplementationClassName = repositoryImplementationName
              Debug = context.Config.IsDebug
              TopLevelConnections = context.Config.TopLevelConnections
              BaseNamespace = baseNamespace
              Namespace = nspaceName
              Commands =
                repository.Commands
                |> Seq.map (generateCommandTemplate context conn)
                |> Seq.toList }

        let repositoryTemplateName = "Repository"

        let loader =
            let assemblyName =
                let assembly = Assembly.GetExecutingAssembly()

                assembly.GetName().Name

            TemplateLoader(assemblyName)

        let template =
            repositoryTemplateName |> loader.GetPath |> loader.Load |> Template.Parse

        let templateContext =
            let templateContext = TemplateContext(TemplateLoader = loader)

            let scriptObject = ScriptObject()
            scriptObject.Import({| Model = templateData |}, renamer = null, filter = null)

            templateContext.PushGlobal scriptObject
            templateContext

        template.Render(templateContext)
