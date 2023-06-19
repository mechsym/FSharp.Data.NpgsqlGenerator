module FSharp.Data.NpgsqlGenerator.Core.EnumGenerator

open System.Data
open System.IO
open System.Reflection
open FSharp.Data.NpgsqlGenerator.Core.Inference
open FSharp.Data.NpgsqlGenerator.Core.TemplateModels
open Humanizer
open Npgsql
open Scriban
open Scriban.Runtime


let rec private readEnum (reader: NpgsqlDataReader) =
    if reader.Read() then
        let id = reader.GetProviderSpecificValue(0) :?> uint32

        let enumName = reader.GetProviderSpecificValue(1) :?> string

        let schemaName = reader.GetProviderSpecificValue(3) :?> string

        let schemaId = reader.GetProviderSpecificValue(2) :?> uint32

        let labels = reader.GetProviderSpecificValue(4) :?> string[]

        let name = enumName.Pascalize()

        { InferredEnum.Oid = id
          Name = name
          ParameterName = enumName.Camelize()
          DbName = enumName
          Schema = schemaName.Pascalize()
          SchemaOid = schemaId
          Labels =
            labels
            |> Array.map (fun dbLabel ->
                { EnumLabel.Label = dbLabel.Pascalize()
                  DbLabel = dbLabel })
          DbSchema = schemaName }
        :: (readEnum reader)
    else
        []

let generate (config: InferredConfiguration) : InferredEnum[] * string =
    let enums =
        use conn = new NpgsqlConnection(config.ConnString)

        conn.Open()

        let text =
            """
            SELECT enum.oid                       AS id
                 , enum.typname                   AS name
                 , enum.typnamespace              AS id_nspace
                 , namespace.nspname              AS schema
                 , ARRAY_AGG(enum_case.enumlabel) AS labels
            FROM pg_type AS enum
                     INNER JOIN pg_namespace AS namespace ON enum.typnamespace = namespace.oid
                     INNER JOIN pg_enum AS enum_case ON enum.oid = enum_case.enumtypid
            WHERE typcategory = 'E'
            GROUP BY enum.oid, enum.typname, enum.typnamespace, namespace.nspname"""

        use command = new NpgsqlCommand(text, conn)

        use reader = command.ExecuteReader(CommandBehavior.Default)

        readEnum reader



    let enumContext =
        enums
        |> List.groupBy (fun enum -> enum.Schema)
        |> List.map (fun (schema, enums) ->
            {| Schema = schema
               Enums = enums
               Namespace = config.UdfNamespace |> Namespace.get |})


    let loader =
        let assemblyName =
            let assembly = Assembly.GetExecutingAssembly()

            assembly.GetName().Name

        TemplateLoader(assemblyName)

    let template = "Enums" |> loader.GetPath |> loader.Load |> Template.Parse

    let templateContext =
        let templateContext = TemplateContext(TemplateLoader = loader)

        let scriptObject = ScriptObject()

        let templateData =
            {| Schemas = enumContext
               Namespace = config.UdfNamespace |> Namespace.get
               Debug = config.IsDebug |}

        scriptObject.Import(templateData, renamer = null, filter = null)

        templateContext.PushGlobal scriptObject
        templateContext

    let result = template.Render(templateContext)


    enums |> List.toArray, result
