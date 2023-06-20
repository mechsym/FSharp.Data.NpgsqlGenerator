# FSharp.Data.NpgsqlGenerator (a.k.a `npgsql-generator`)

## What is it

`npgsql-generator` is a `dotnet` SDK tool that tries to mix the best aspects of type providers, source generators and
dapper to provide a convenient **type safe** and very fast ORM solution that is **unit testable**.

### How it works

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

## Usage

### Installation

Since it is a .NET SDK tool, you can install it simply by typing:

```shell
> dotnet new tool-manifest # if you haven't done already
> dotnet tool install npgsql-generator
```

...and that's it. Now you can invoke it by `dotnet npgsql-generator`. The tool has rich CLI interface with extensive
help so whenever you are stuck, just add `--help` to the command line and the tool will print detailed usage
information.

### Concepts

`npgsql-generator` operates with very similar concepts to ORM solutions in the generated code. It generates repositories
for you. One repository is a set of operations that relate to the same database entity. For instance, `UserRepository`
collects all the operations related to `user` table. `DocumentRepository` operates on table `document` and so on.

As the input for `npgsql-generator`, you have to provide plain .sql files. One sql file per repository that you would
like to generate. The name of each repository file has a special meaning. We derive the generated repository name and
its container namespace from the file name therefore repository file names should follow this pattern:

```
<namespace>.<repository_name>.sql
```

For instance, the file name `My.Favorite.Namespace.User.sql` would result in a repository `UserRepository` in
namespace `My.Favorite.Namespace`.

#### Repository file structure

Like it was mentioned before, the repository file is a plain sql file that your IDE is supposed to understand. There are
some extra twists however. The repository file is a list of SQL queries, separated by the regular delimiter that
postgres understands: `;`. You have to provide one query per each operation that you would like `npgsql-generator` to
generate a function and input/output types for.

Enough of talking, one operation looks like this:

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
not, `npgsql-generator` can help you in adding these but eventually, you will just copy paste this from existing
operations in your repository files). This json contains some metadata about the operation:

- `name`: name of the generated F# function
- `isPrepared`: if true, `npgsql-generator` will generate a reusable prepared statement for you
- `singleRow`: if true, the return type of the generated function will be `'a option` and not `'a seq`. So set it to
  true if you expect one row to be returned.

Right after the metadata section comes the SQL query itself. Which should give no surprise. The only difference compared
to regular SQL scripts is the possibility to provide parameters using `@` character, like `@ids` above. The syntax for
the command text is the same as `NpgslCommand.CommandText` as this query is literally being passed to it.

### Generating code

Once you finished adding the operations to the repository file, it's time to generate code.

Let's say you saved the above `GetDocumentsByIds` operation to a file called `Cms.Repositories.Document.sql` then you
can invoke the code generator like this:

```shell
dotnet npgsql-generator generate all -c "Host=localhost;UserName=postgres;Password=postgres;Database=cms" Cms.Repositories.Document.sql
```

And it will generate an F# file that you can directly include in your F# project:

`Cms.Repositories.Document.g.fs`

The file contains the generated repository and you are done.

#### Types.fs

if you used the parameter `all` like in the above example, `npgsql-generator` will generate another file for you. In the
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

Optionally, you can add:

- `--prepared` flag to generate a prepared command
- `--single-row` flag to generate a command that returns a single row

#### `generate`

`generate` accepts one of 3 possible subcommands: `types`, `repositories` or `all`. `types` will generate only the
auxiliary file that makes it possible to operate with user defined enums. `repositories` will generate the repository
files. `all` will generate both. It was necessary to have the 3 options because you may want to
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
  you set this flag, the generated Repository will accept the connection and not the individual operations. (= the
  generated interfaces are 100% decoupled from even Npgsql)
- `--record-return-types`: normally each operation will return an anonymous record. If you set this flag, non-anonymous
  records will be generated.

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
