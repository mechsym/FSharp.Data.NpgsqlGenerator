namespace FSharp.Data.NpgsqlGenerator.Core.Generator.Schema

open System
open System.Collections
open System.Net
open Npgsql.PostgresTypes
open NpgsqlTypes


module internal Builtins =

    let private bar: Npgsql.NpgsqlDataReader = null

    let getters =
        [ "boolean", nameof (bar.GetBoolean)
          "bool", nameof (bar.GetBoolean)

          "smallint", nameof (bar.GetInt16)
          "int2", nameof (bar.GetInt16)
          "integer", nameof (bar.GetInt32)
          "int", nameof (bar.GetInt32)
          "int4", nameof (bar.GetInt32)
          "bigint", nameof (bar.GetInt64)
          "int8", nameof (bar.GetInt64)

          "real", nameof (bar.GetFloat)
          "float4", nameof (bar.GetFloat)
          "double precision", nameof (bar.GetDouble)
          "float8", nameof (bar.GetDouble)

          "numeric", nameof (bar.GetDecimal)
          "decimal", nameof (bar.GetDecimal)
          "money", nameof (bar.GetDecimal)
          "text", nameof (bar.GetString)

          "character varying", nameof (bar.GetString)
          "varchar", nameof (bar.GetString)
          "character", nameof (bar.GetString)
          "char", nameof (bar.GetString)

          "citext", nameof (bar.GetString)
          "jsonb", nameof (bar.GetString)
          "json", nameof (bar.GetString)
          "xml", nameof (bar.GetString)

          "date", nameof (bar.GetDateTime)
          "interval", nameof (bar.GetTimeSpan)
          "timestamp without time zone", nameof (bar.GetDateTime)
          "timestamp", nameof (bar.GetDateTime)
          "timestamp with time zone", nameof (bar.GetDateTime)
          "timestamptz", nameof (bar.GetDateTime)
          "time without time zone", nameof (bar.GetTimeSpan)
          "time", nameof (bar.GetTimeSpan)
          "time with time zone", nameof (bar.GetTimeSpan)
          "timetz", nameof (bar.GetDateTime)

          //special value
          "enum", nameof (bar.GetString) ]
        |> Map.ofList

    let private builtins =
        [ "boolean", typeof<bool>
          "bool", typeof<bool>

          "smallint", typeof<int16>
          "int2", typeof<int16>
          "integer", typeof<int32>
          "int", typeof<int32>
          "int4", typeof<int32>
          "bigint", typeof<int64>
          "int8", typeof<int64>

          "real", typeof<single>
          "float4", typeof<single>
          "double precision", typeof<double>
          "float8", typeof<double>

          "numeric", typeof<decimal>
          "decimal", typeof<decimal>
          "money", typeof<decimal>
          "text", typeof<string>

          "character varying", typeof<string>
          "varchar", typeof<string>
          "character", typeof<string>
          "char", typeof<string>

          "citext", typeof<string>
          "jsonb", typeof<string>
          "json", typeof<string>
          "xml", typeof<string>
          "point", typeof<NpgsqlPoint>
          "lseg", typeof<NpgsqlLSeg>
          "path", typeof<NpgsqlPath>
          "polygon", typeof<NpgsqlPolygon>
          "line", typeof<NpgsqlLine>
          "circle", typeof<NpgsqlCircle>
          "box", typeof<bool>

          "bit", typeof<BitArray>
          "bit(n)", typeof<BitArray>
          "bit varying", typeof<BitArray>
          "varbit", typeof<BitArray>

          "hstore", typeof<IDictionary>
          "uuid", typeof<Guid>
          "cidr", typeof<ValueTuple<IPAddress, int>>
          "inet", typeof<IPAddress>
          "macaddr", typeof<NetworkInformation.PhysicalAddress>
          "tsquery", typeof<NpgsqlTsQuery>
          "tsvector", typeof<NpgsqlTsVector>

          "date", typeof<DateTime>
          "interval", typeof<TimeSpan>
          "timestamp without time zone", typeof<DateTime>
          "timestamp", typeof<DateTime>
          "timestamp with time zone", typeof<DateTime>
          "timestamptz", typeof<DateTime>
          "time without time zone", typeof<TimeSpan>
          "time", typeof<TimeSpan>
          "time with time zone", typeof<DateTimeOffset>
          "timetz", typeof<DateTimeOffset>

          "bytea", typeof<byte[]>
          "oid", typeof<UInt32>
          "xid", typeof<UInt32>
          "cid", typeof<UInt32>
          "oidvector", typeof<UInt32[]>
          "name", typeof<string>
          "char", typeof<string>

          "regtype", typeof<UInt32>
          "regclass", typeof<UInt32>
          //"range", typeof<NpgsqlRange>, NpgsqlDbType.Range)
          ]
        |> Map.ofList


    let getTypeMapping (datatype: string) : Type =
        builtins |> Map.tryFind datatype |> Option.defaultValue typeof<obj>

module internal PostgresType =
    let rec toClrType (this: PostgresType) =
        match this with
        | :? PostgresBaseType as x -> Builtins.getTypeMapping x.Name
        | :? PostgresEnumType -> typeof<string>
        | :? PostgresDomainType as x -> x.BaseType |> toClrType
        | :? PostgresArrayType as arr -> (arr.Element |> toClrType).MakeArrayType()
        | _ -> typeof<obj>


type internal DataType =
    { Name: string
      Schema: string
      IsNullable: bool
      ClrType: Type }

module internal DataType =
    let shortNames =
        [ typeof<Byte>, "byte"
          typeof<Int16>, "int16"
          typeof<Boolean>, "bool"
          typeof<Int32>, "int"
          typeof<Int64>, "int64"
          typeof<Single>, "float"
          typeof<Double>, "double"
          typeof<Char>, "char"
          typeof<String>, "string"
          typeof<Object>, "obj"

          typeof<Decimal>, "decimal"
          typeof<SByte>, "sbyte"
          typeof<UInt16>, "uint16"
          typeof<UInt32>, "uint"
          typeof<UInt64>, "uint64" ]
        |> List.map (fun (ttype, shortName) -> ttype.FullName, shortName)
        |> Map.ofList

    let fullName this = sprintf "%s.%s" this.Schema this.Name

    let isUserDefinedType this = this.Schema <> "pg_catalog"

    let finalClrType (this: DataType) : string =
        let name =
            shortNames
            |> Map.tryFind this.ClrType.FullName
            |> Option.defaultValue this.ClrType.Name

        if this.IsNullable then $"%s{name} option" else name

    let from (isNullable: bool) (postgresType: PostgresType) =
        { DataType.Name = postgresType.Name
          Schema = postgresType.Namespace
          ClrType = postgresType |> PostgresType.toClrType
          IsNullable = isNullable }
