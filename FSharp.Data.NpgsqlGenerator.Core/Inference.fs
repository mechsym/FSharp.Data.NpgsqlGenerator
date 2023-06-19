namespace FSharp.Data.NpgsqlGenerator.Core.Inference

type CommandName = private CommandName of string

module CommandName =
    let from (value: string) = CommandName value

    let get (CommandName value) : string = value

type CommandText = private CommandText of string

module CommandText =
    let from (value: string) = CommandText value

    let get (CommandText value) : string = value

type RepositoryName = private RepositoryName of string

module RepositoryName =
    let from (value: string) = RepositoryName value

    let get (RepositoryName value) : string = value

type Namespace = private Namespace of string

module Namespace =
    let from (value: string) = Namespace value

    let get (Namespace value) : string = value

type InferredCommand =
    { Name: CommandName
      Text: CommandText
      SingleRow: bool
      Prepared: bool }

type InferredRepository =
    { Name: RepositoryName
      Namespace: Namespace
      Commands: InferredCommand seq }

type InferredConfiguration =
    { ConnString: string
      UdfNamespace: Namespace
      TopLevelConnections: bool
      AnonymousReturnTypes: bool
      IsDebug: bool }
