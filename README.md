# npgsql-generator

## Description

`npgsql-generator` is a `dotnet` SDK tool that mixes the best aspects of [type providers](https://github.com/demetrixbio/FSharp.Data.Npgsql), source generators and
[Dapper](https://github.com/DapperLib/Dapper) to provide a convenient, **type safe** and very fast ORM solution that is **unit testable**.

## How it works

1. You provide SQL queries in a Postgres flavoured .sql script file, each query is enriched with some further JSON metadata that gives more information to the generator about the query:

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

- since this is a valid .sql file, your favorite IDE can give you assistance in editing these files
- there is no need to write the json metadata by hand, `npgsql-generator` can also generate that for you

2. Then you run `npgsql-generator` tool which

- infers the type and nullability of input and output parameters
- based on preferences, generates corresponding anonymous, or non-anonymous records to read the output into
- generates functions that execute the commands using plain, low level `Npgsql` code, without adding further dependencies. Basically does all the ceremony around Npgsql.

The generated code looks like this:

```fsharp
type IUserRepository =
    abstract member GetUserByEmail: conn: NpgsqlConnection -> email: string -> Task<{| Id: int; FirstName: string option; LastName: string option; Slug: string; |} option>

module UserRepository =

    let create () =
        { new IUserRepository with
            override this.GetUserByEmail (conn: NpgsqlConnection) (email: string) =
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

## Motivations

If you look closer, `npgsql-generator` highly resembles a type provider project, in fact, it was grown out of an
existing type provider project: [FSharp.Data.Npgsql](https://github.com/demetrixbio/FSharp.Data.Npgsql). Quite some
code, especially the inference was taken from there so `FSharp.Data.Npgsql` could be considered as the spiritual
ancestor of `npgsql-generator`. (many thanks to its authors and contributors!)

Despite type providers being a brilliant idea in general as they give type safe data access that not many other solutions do, 
there were quite some lessons learnt while working with them. The most important one was how much perf overhead they impose on 
the IDE if you have a project of a certain size. `npgsql-generator` is trying to mitigate that overhead by sacrificing some 
developer convenience by moving the type generation to build time instead of design time while keeping the essence of 
type providers: type safety. This results in a bit less instant feedback loop that you are used to when using type providers but also
results in a much more predictable IDE performance while editing F# code. 

Additionally, you can get IDE help for the SQL itself which was not possible with type providers. You had to edit 
the SQL externally if you wanted IDE help and copy the final text to the F# codebase.

Apart from that, the generated code uses interfaces that your code can rely on so an additional benefit is a much less
coupled code with data access layer, compared to type providers. Unit testing became possible!

## Usage

### Installation

Since it is an ordinary .NET SDK tool, it could be installed by typing:

```shell
> dotnet new tool-manifest # in case tool manifest is not yet added
> dotnet tool install npgsql-generator
```

...and that's it. Now the tool could be invoked by running `dotnet npgsql-generator`. 

The tool has rich CLI interface with extensive
help so in case not sure how to move forward, just add `--help` to the command or subcommand and the tool will print detailed usage
information.

### Concepts

`npgsql-generator` operates with very similar concepts/terminology to traditional ORM solutions. It generates *repositories*. 
One *repository* is a set of *operations* that are related to the same database entity. For instance, `UserRepository`
collects all the operations related to `user` table. `DocumentRepository` operates on table `document` and so on.

As the input for `npgsql-generator`, plain .sql files have to be provided. One sql file per repository. 
The name of the repository file has a special meaning. `npgsql-generator` derives the generated repository name and
its container namespace from the file name therefore repository file names should follow this pattern:

```
<namespace>.<repository_name>.sql
```

For instance, the file name `My.Favorite.Namespace.User.sql` would result in a repository `UserRepository` in
namespace `My.Favorite.Namespace`.

#### Repository file structure

As it was mentioned previously, the repository file is a plain sql file that an IDE is supposed to understand. There are
some restrictions however. The repository file is a list of SQL queries, separated by the regular delimiter that
postgres understands: `;`. As a rule of thumb, you have to provide one query for each operation that you would like `npgsql-generator` to
generate a function and input/output types for.

For instance:

```sql 
/* 
{
  "name": "GetDocumentsByIds",
  "isPrepared": false,
  "singleRow": false
}
*/
SELECT id
     , created
     , updated
     , type
FROM cms.document
WHERE id = ANY (@ids);
```

#### The anatomy of an operation

Each operation is preceded by a `/* */` comment section and this comment section contains a small json object. This json 
contains some metadata about the operation. `npgsql-generator` can help in generating this json but otherwise it is also 
easy to just copy paste the json between queries.

The content of the json object:

- `name`: name of the generated F# function
- `isPrepared`: if true, `npgsql-generator` will generate a reusable prepared statement
- `singleRow`: if true, the return type of the generated function will be `'a option` and not `'a seq`. So set it to
  true if the operation is expected to return at most one row.

Right after the metadata section comes the SQL query itself. The syntax of the query follows postgres sql syntax with one difference:
it is possible to provide parameters using `@` character, like `@ids` in the above example. Basically, the syntax for
the query is the same as `NpgslCommand.CommandText` property as the query is literally being passed to it eventually.

### Generating code

Once the repository file is ready, it's time to generate code.

Let's say the `GetDocumentsByIds` operation in the above example is saved to a file called `Cms.Repositories.Document.sql` then 
the generator should be invoked like that:

```shell
dotnet npgsql-generator generate all -c "Host=localhost;UserName=postgres;Password=postgres;Database=cms" Cms.Repositories.Document.sql
```

And it will generate an F# file that could be included in an F# project right away:

`Cms.Repositories.Document.g.fs`

The file contains the F# version of the repository.

#### Types.fs

In case `npgsql-generator generate` was invoked with a subcommand `all` or `types`, `npgsql-generator` will generate another file. In the
above example, the file would be called `Db.g.fs`. `GetDocumentsByIds` contains an enum like value in the select list: `type`. It has
type `document_type` in the database:

```sql
create type document_type as enum ('news', 'news_category', 'event', 'product', 'brand', 'knowledge_base_article', 'knowledge_base_category', 'gallery');
```

`npgsql-generator` infers and reads user defined enums from the database and generates strongly typed access even for `document_type`. 

`Db.g.fs` file contains the generated code for handling those types:

```fsharp
namespace Db.Types

/// document_type
[<RequireQualifiedAccess>]
type DocumentType =
    /// event
    | Event
    /// product
    | Product
    /// brand
    | Brand
    /// knowledge_base_article
    | KnowledgeBaseArticle
    /// knowledge_base_category
    | KnowledgeBaseCategory
    /// gallery
    | Gallery
    /// page
    | Page
    /// news
    | News
    /// news_category
    | NewsCategory

...auxiliary functions that convert the database enum value to f# union
```

### Available commands and configuration options

There are 3 main commands that the tool supports: `create-repository`, `create-command` and `generate`.

#### `create-repository`

This command could be used to create a new repository file:

```shell
> dotnet npgsql-generator create-repository --namespace Foo.Bar --output Out Baz
```

This will create a new repository with name `Baz` in namespace `Foo.Bar` and place it to `Out` directory. 

The below flags could be omitted:
- `--namespace` flag, and the generated namespace will be `Global`
- `--output` flag, and the generated code will be placed in the current directory

#### `create-command`

This command could be used to add a new operation to an existing repository file:

```shell
> dotnet npgsql-generator create-command --repository Foo.Bar.Baz.sql MyFavoriteCommand
```

This will append a new command with name `MyFavoriteCommand` to repository `Foo.Bar.Baz.sql`.

Optionally, these flags are supported:

- `--prepared` flag to generate a prepared command
- `--single-row` flag to generate a command that returns a single row

#### `generate`

`generate` accepts one of 3 possible subcommands: `types`, `repositories` or `all`. `types` will generate only the
auxiliary file that makes it possible to operate with user defined enums. `repositories` will generate the repository
files. `all` will generate both. It was necessary to have the 3 options because You may want to
generate `types` and `repositories` differently.

Each subcommand support a different set of options, for further reference, please use:

```shell
> dotnet npgsql-generator generate <command> --help
```

Here is an example invocation:

```shell
> dotnet npgsql-generator generate all --connection-string "Host=localhost;UserName=postgres;Password=postgres;Database=cms" \
 Foo.Bar.Repository1.sql \
 Foo.Bar.Repository2.sql \
 Foo.Bar.Repository3.sql

```

This command will generate the repository files for `Foo.Bar.Repository1-2-3.sql` definitions using the provided
connection string.

It accepts a few further optional flags:

- `--udf-namespace`: the namespace to put the enum types into
- `--output-path`: where to place the generated files
- `--top-level-connections`: normally each operation accepts an `NpgsqlConnection` parameter in the generated code. If
  this flag has been set, the generated Repository will accept the connection and not the individual operations. (= the
  generated interfaces are 100% decoupled from even Npgsql)
- `--record-return-types`: normally each operation will return an anonymous record. If this flag has been set, non-anonymous
  records will be generated.

## Comparison with other solutions

From the below comparison, it clearly stands out that most of the statements could be seen both as positive or negative
thing so whether `npgsql-generator` is for you highly depends on your preference and the type of your project.

### Type providers

- unlike with type providers, you get IDE help, code completions when writing SQL since you are actually editing a Sql
  script
- the generated code is plain Npgsql code which everyone is familiar with, there is no hidden dll somewhere on your computer
- you can debug the generated code
- scales better on larger projects: in fact, the schema in the database changes very rarely, there is no need to
  constantly do roundtrips between the language server and the database to determine changes. However, on smaller
  projects, the ceremony that is required to set up `npgsql-generator` might be more than setting up a type provider.
- your code is much less coupled with db code, the `npgsql-generator` generates interfaces that your code can depend on
- unit testing is possible
- it's not necessary to have a running postgres database on your CI if You don't want
- no runtime dependency, only Npgsql, and you are in charge for providing it

### EF and traditional ORM frameworks

- with `npgsql-generator` you are in full control while traditional ORM solutions remove a lot of burden from you in
  exchange for some additional overhead (this could be seen both as a negative or positive thing)
- compared with *code-first* and *database-first* approaches, `npgsql-generator` sits in between as you are responsible
  for
  shaping your database schema (similar to database-first) but also you are guarded by type safety (similar to
  code-first)
- EF supports multiple database platforms while `npgsql-generator` does not and that is unlikely to change
- Entity Framework and traditional ORM frameworks could be heavy and add quite some overhead due to internal
  synchronization and state management while `npgsql-generator` imposes no overhead at all compared to a situation where
  you write Your own `Npgsql` code manually

### Dapper

- `npgsql-generator` generates **type safe** code, schema changes in the database are automatically picked up. You
  are alone when using `Dapper` in this case
- `Dapper` is more flexible, it supports dynamic queries while `npgsql-generator` does not. However, `Dapper`
  and `npgsql-generator` can coexist in the same project and you can rely on Dapper for dynamic queries and
  on `npgsql-generator` for non dynamic ones
- `Dapper` supports multiple database platforms while `npgsql-generator` does not and that is unlikely to change
