# FSharp.Data.NpgsqlGenerator

## What?

`npgsql-generator` is a `dotnet` SDK tool that tries to mix the best aspects of type providers, source generators and
dapper to provide a convenient **type safe** and very fast ORM solution.

### How it works (on a high level)

1. you provide SQL queries in a file (which is an absolutely valid .sql script, so you can get help from your favorite
   IDE in editing), enriched with some JSON metadata (the tool gives you help with generating the metadata):

```sql
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

If you look closer, `npgsql-generator` highly resembles a type provider project, in fact, it was grown out of an
existing type provider project: [FSharp.Data.Npgsql](https://github.com/demetrixbio/FSharp.Data.Npgsql). Quite some
code, especially the inference was taken from there so `FSharp.Data.Npgsql` could be considered as the spiritual
ancestor of `npgsql-generator`. (many thanks to its authors and contributors!)

There were quite some lessons learnt while working with `FSharp.Data.Npgsql` and with type providers in general and the
most important one was how much perf overhead they impose on the IDE if you have a project of a certain
size. `npgsql-generator` is
trying to mitigate that overhead sacrificing some developer comfort by moving the type generation to build time instead
of design time. This results in a bit less instant feedback loop that you are used to when using type providers but also
results in a much more predictable IDE performance while editing your F# code.
Apart from that, the generated code uses interfaces that your code can rely on so an additional benefit is a much less
coupled code with data access layer, compared to type providers.

### Comparison with other solutions

From the below comparison, it clearly stands out that most of the statements could be seen both as positive or negative
thing so whether `npgsql-generator` is for you highly depends on your preference and the type of your project.

#### Type providers

- unlike with type providers, you get IDE help, code completions when writing SQL since you are actually editing a Sql
  script
- the generated code is plain Npgsql which everyone is familiar with
- you can debug the generated code
- scales better on larger projects: in fact, the schema in the database changes very rarely, there is no need to
  constantly do roundtrips between the language server and the database to determine changes. However, on smaller
  projects `npgsql-generator` might require more ceremony to set up.
- your code is much less coupled with db code, the `npgsql-generator` generates interfaces that your code can depend on
- unit testing became possible with `npgsql-generator`
- it's not necessary to have a running postgres database on your CI if you don't want
- no runtime dependency, only Npgsql, and you are in charge for providing it

#### EF and traditional ORM frameworks

- with `npgsql-generator` you are in full control while traditional ORM solutions remove a lot of burden from you in
  exchange for some additional overhead (this could be seen both as a negative or positive thing)
- compared with *code-first* and *database-first* approaches, `npgsql-generator` sits in between as you are responsible
  for
  shaping your database schema (similar to database-first) but also you are guarded by type safety (similar to
  code-first)
- EF supports multiple database platforms while `npgsql-generator` does not and that is unlikely to change
- Entity Framework and traditional ORM frameworks could be heavy and add quite some overhead due to internal
  synchronization and state management while `npgsql-generator` imposes no overhead at all compared to a situation where
  you write your own `Npgsql` code manually

#### Dapper

- `npgsql-generator` generates **type safe** code, schema changes in the database are automatically picked up while you
  are alone with `Dapper` in this case
- `Dapper` is more flexible, it supports dynamic queries while `npgsql-generator` does not. However, `Dapper`
  and `npgsql-generator` can coexist in the same project and you can rely on Dapper for dynamic queries and
  on `npgsql-generator` for non dynamic ones
- `Dapper` supports multiple database platforms while `npgsql-generator` does not and that is unlikely to change

## How?

The package is available on [Nuget.org](https://www.nuget.org/packages/npgsql-generator)