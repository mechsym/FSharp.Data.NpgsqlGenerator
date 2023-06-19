# FSharp.Data.NpgsqlGenerator

## What?

`npgsql-generator` is a `dotnet` SDK tool that tries to mix the best aspects of type providers, source generators and dapper to provide a convenient and very fast ORM solution.

### How it works (on a high level)

1. you provide SQL queries in a file (which is an absolutely valid .sql script, so you can get help from your favorite IDE in editing), enriched with some JSON metadata (the tool gives you help with generating the metadata):

```postgresql
/*
{
  "name": "GetUserByEmail",
  "isPrepared": false,
  "singleRow": true 
}
*/
select id, first_name, last_name, slug
from cms.user
where email = @email;
```

2. run the tool which:
- infers the type and nullability of input and output parameters
- generates corresponding anonymous (or non-anonymous if you want) records to read the output into
- generates functions that execute your command using plain, low level `Npgsql` code, no further dependencies.
3. the tool spits out such an F# code for you:

```fsharp
type IUserRepository =
    abstract member GetUserByEmail: conn: NpgsqlConnection -> email: string -> Task<{| Id: int; FirstName: string option; LastName: string option; Slug: string; |} option>

module UserRepository =

    let create () =
        { new IUserRepository with
            override this.GetUserByEmaail (conn: NpgsqlConnection) (email: string) =
                use command = conn.CreateCommand()
                command.CommandText <- """select id, first_name, last_name, slug
            from cms.user
            where email = @email;"""
                command.Parameters.Add(NpgsqlParameter(
                    ParameterName = "email",
                    DataTypeName = "text",
                    Value = email
                ))
                |> ignore
                task {
                    use! reader = command.ExecuteReaderAsync()
                    let! rowRead = reader.ReadAsync()
                    if rowRead then
                        return Some({|
                            Id = reader.GetInt32(0)
                            FirstName =  
                                if reader.IsDBNull(1) then None
                                else Some(reader.GetString(1))
                            LastName =  
                                if reader.IsDBNull(2) then None
                                else Some(reader.GetString(2))
                            Slug = reader.GetString(3)
                        |}) 
                    else
                        return None
                }

        }

    let instance = create ()

```

4. now you can even check the generated code into your version control

## Why?

## How?

The package is available on [Nuget.org](https://www.nuget.org/packages/npgsql-generator)