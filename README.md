# FSharp.Data.NpgsqlGenerator (a.k.a `npgsql-generator`)

## What is it

`npgsql-generator` is a `dotnet` SDK tool that tries to mix the best aspects of [type providers](https://github.com/demetrixbio/FSharp.Data.Npgsql), source generators and
[Dapper](https://github.com/DapperLib/Dapper) to provide a convenient, **type safe** and very fast ORM solution that is **unit testable**.

### How it works

1. You provide SQL queries in a file (which is an absolutely valid .sql script, so You can get help from Your favorite
   IDE in editing), enriched with some JSON metadata (the `npgsql-generator` gives You help with generating the JSON):

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

2. run `npgsql-generator` tool which

- infers the type and nullability of input and output parameters
- generates corresponding anonymous (or non-anonymous if You want) records to read the output into
- generates functions that execute Your command using plain, low level `Npgsql` code, without further dependencies

3. the resulting generated F# code looks like this:

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

## Usage

### Installation

Since it is a .NET SDK tool, You can install it simply by typing:

```shell
> dotnet new tool-manifest # if You haven't done already
> dotnet tool install npgsql-generator
```

...and that's it. Now You can invoke it by `dotnet npgsql-generator`. 

The tool has rich CLI interface with extensive
help so whenever You are stuck, just add `--help` to the command line and the tool will print detailed usage
information.

### Concepts

`npgsql-generator` operates with very similar concepts/terminology to ORM solutions. It generates *repositories*
for You. One *repository* is a set of *operations* that are related to the same database entity. For instance, `UserRepository`
collects all the operations related to `user` table. `DocumentRepository` operates on table `document` and so on.

As the input for `npgsql-generator`, You have to provide plain .sql files. One sql file per repository. 
The name of the repository file has a special meaning. `npgsql-generator` derives the generated repository name and
its container namespace from the file name therefore repository file names should follow this pattern:

```
<namespace>.<repository_name>.sql
```

For instance, the file name `My.Favorite.Namespace.User.sql` would result in a repository `UserRepository` in
namespace `My.Favorite.Namespace`.

#### Repository file structure

Like it was mentioned before, the repository file is a plain sql file that Your IDE is supposed to understand. There are
some extra twists however. The repository file is a list of SQL queries, separated by the regular delimiter that
postgres understands: `;`. You have to provide one query per each operation that You would like `npgsql-generator` to
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

Let's look at the anatomy of such an operation.

Each operation is preceded by a `/* */` comment section and the comment section contains a small json snippet (fear
not, `npgsql-generator` can help You in adding these but eventually, You will just copy paste this from existing
operations in Your repository files). This json contains some metadata about the operation:

- `name`: name of the generated F# function
- `isPrepared`: if true, `npgsql-generator` will generate a reusable prepared statement for You
- `singleRow`: if true, the return type of the generated function will be `'a option` and not `'a seq`. So set it to
  true if You expect one row to be returned.

Right after the metadata section comes the SQL query itself. Which should give no surprise. The only difference compared
to regular SQL scripts is the possibility to provide parameters using `@` character, like `@ids` above. The syntax for
the command text is the same as `NpgslCommand.CommandText` as this query is literally being passed to it.

### Generating code

Once You are finished with adding the operations to the repository file, it's time to generate code.

Let's say You saved the above `GetDocumentsByIds` operation to a file called `Cms.Repositories.Document.sql` then You
can invoke the code generator like this:

```shell
dotnet npgsql-generator generate all -c "Host=localhost;UserName=postgres;Password=postgres;Database=cms" Cms.Repositories.Document.sql
```

And it will generate an F# file that You can directly include in Your F# project:

`Cms.Repositories.Document.g.fs`

The file contains the generated repository, You are done.

#### Types.fs

if You used the parameter `all` like in the above example, `npgsql-generator` will generate another file for You. In the
above case, it is called `Db.g.fs`. `GetDocumentsByIds` contains an enum like value in the select list: `type`. It has
type `document_type` in the database. `npgsql-generator` infers and reads user defined enums from the database and
generates strongly typed access even for that. `Db.g.fs` contains the generated code for handling those:

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

...auxiliary functions that operate on DocumentType
```

### Available commands and configuration options

There are 3 main commands that the tool supports: `create-repository`, `create-command` and `generate`.

#### `create-repository`

You can use this command to create a new repository file:

```shell
> dotnet npgsql-generator create-repository --namespace Foo.Bar --output Out Baz
```

This will create a new repository with name `Baz` in namespace `Foo.Bar` and place it to `Out` directory. You can omit

- the `--namespace` flag and the generated namespace will be `Global`
- the `--output` flag and the generated code will be placed in the current directory

#### `create-command`

You can use this command to add a new operation to an existing repository file:

```shell
> dotnet npgsql-generator create-command --repository Foo.Bar.Baz.sql MyFavoriteCommand
```

This will append a new command with name `MyFavoriteCommand` to repository `Foo.Bar.Baz.sql`.

Optionally, You can add:

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

Now let's see one of them:

```shell
> dotnet npgsql-generator generate all --connection-string "Host=localhost;UserName=postgres;Password=postgres;Database=cms" \
 Foo.Bar.Repository1.sql \
 Foo.Bar.Repository2.sql \
 Foo.Bar.Repository3.sql

```

This command will generate the repository files for `Foo.Bar.Repository1-2-3.sql` definitions using the provided
connection string.

It accepts a few further options:

- `--udf-namespace`: the namespace to put the enum types into
- `--output-path`: where to place the generated files
- `--top-level-connections`: normally each operation accepts an `NpgsqlConnection` parameter in the generated code. If
  You set this flag, the generated Repository will accept the connection and not the individual operations. (= the
  generated interfaces are 100% decoupled from even Npgsql)
- `--record-return-types`: normally each operation will return an anonymous record. If You set this flag, non-anonymous
  records will be generated.

## Why?

If You look closer, `npgsql-generator` highly resembles a type provider project, in fact, it was grown out of an
existing type provider project: [FSharp.Data.Npgsql](https://github.com/demetrixbio/FSharp.Data.Npgsql). Quite some
code, especially the inference was taken from there so `FSharp.Data.Npgsql` could be considered as the spiritual
ancestor of `npgsql-generator`. (many thanks to its authors and contributors!)

There were quite some lessons learnt while working with `FSharp.Data.Npgsql` and with type providers in general and the
most important one was how much perf overhead they impose on the IDE if You have a project of a certain
size. `npgsql-generator` is
trying to mitigate that overhead by sacrificing some developer convenience by moving the type generation to build time instead
of design time. This results in a bit less instant feedback loop that You are used to when using type providers but also
results in a much more predictable IDE performance while editing Your F# code. 

Additionally, You can get IDE help for the SQL 
itself which was not possible with type providers. You had to edit the SQL externally if you wanted IDE help and copy the final text to the F# codebase.

Apart from that, the generated code uses interfaces that Your code can rely on so an additional benefit is a much less
coupled code with data access layer, compared to type providers. Unit testing became possible!

### Comparison with other solutions

From the below comparison, it clearly stands out that most of the statements could be seen both as positive or negative
thing so whether `npgsql-generator` is for You highly depends on Your preference and the type of Your project.

#### Type providers

- unlike with type providers, You get IDE help, code completions when writing SQL since You are actually editing a Sql
  script
- the generated code is plain Npgsql code which everyone is familiar with, there is no hidden dll somewhere on your computer
- You can debug the generated code
- scales better on larger projects: in fact, the schema in the database changes very rarely, there is no need to
  constantly do roundtrips between the language server and the database to determine changes. However, on smaller
  projects, the ceremony that is required to set up `npgsql-generator` might be more than setting up a type provider.
- Your code is much less coupled with db code, the `npgsql-generator` generates interfaces that Your code can depend on
- unit testing is possible
- it's not necessary to have a running postgres database on Your CI if You don't want
- no runtime dependency, only Npgsql, and You are in charge for providing it

#### EF and traditional ORM frameworks

- with `npgsql-generator` You are in full control while traditional ORM solutions remove a lot of burden from You in
  exchange for some additional overhead (this could be seen both as a negative or positive thing)
- compared with *code-first* and *database-first* approaches, `npgsql-generator` sits in between as You are responsible
  for
  shaping Your database schema (similar to database-first) but also You are guarded by type safety (similar to
  code-first)
- EF supports multiple database platforms while `npgsql-generator` does not and that is unlikely to change
- Entity Framework and traditional ORM frameworks could be heavy and add quite some overhead due to internal
  synchronization and state management while `npgsql-generator` imposes no overhead at all compared to a situation where
  You write Your own `Npgsql` code manually

#### Dapper

- `npgsql-generator` generates **type safe** code, schema changes in the database are automatically picked up. You
  are alone when using `Dapper` in this case
- `Dapper` is more flexible, it supports dynamic queries while `npgsql-generator` does not. However, `Dapper`
  and `npgsql-generator` can coexist in the same project and You can rely on Dapper for dynamic queries and
  on `npgsql-generator` for non dynamic ones
- `Dapper` supports multiple database platforms while `npgsql-generator` does not and that is unlikely to change
