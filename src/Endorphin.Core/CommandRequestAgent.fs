// Copyright (c) University of Warwick. All Rights Reserved. Licensed under the Apache License, Version 2.0. See LICENSE.txt in the project root for license information.

namespace Endorphin.Core

open System
open log4net

[<AutoOpen>]
/// Generic command/request agent with error handling used to serialise posted commands and
/// requests which are defined by closures. Useful in serialising communications to an
/// instrument with a C API.
module CommandRequestAgent = 
    
    [<AutoOpen>]
    /// Command/request agent model.
    module Model =

        /// Agent message, defining a command or request. Each message case is parameteraised by
        /// an async workflow  dependent on the agent handle which is created during initialisation. 
        /// Each command or request may fail, so the return type of the contained workflows is of
        /// Choice<'Result, exn>. If the the failure case is returned, the exception is raised by
        /// the function which posts the command or request to the agent. Commands do not return a
        /// vaule in the success case, so the result type is unit. Requests can either return a
        /// value type or an object type. Command and request messages also include a description
        /// which is used for logging. Finally, the close message notifies the agent that it
        /// should stop processing messages.
        type internal CommandRequestMessage<'Handle> = 
            | Command       of description : string * commandFunc : ('Handle -> Async<Choice<unit,      exn>>) * replyChannel : AsyncReplyChannel<Choice<unit,      exn>>
            | ValueRequest  of description : string * requestFunc : ('Handle -> Async<Choice<ValueType, exn>>) * replyChannel : AsyncReplyChannel<Choice<ValueType, exn>>
            | ObjectRequest of description : string * requestFunc : ('Handle -> Async<Choice<obj,       exn>>) * replyChannel : AsyncReplyChannel<Choice<obj,       exn>>
            | Close         of                        closeFunc   : ('Handle -> Async<Choice<unit,      exn>>) * replyChannel : AsyncReplyChannel<Choice<unit,      exn>>
    
        /// A command/request agent processes messages of CommandRequestMessage type. The details
        /// of the implementation are hidden from the user of the library.
        type CommandRequestAgent<'Handle> = internal CommandRequestAgent of Agent<CommandRequestMessage<'Handle>>

    [<RequireQualifiedAccess>]
    /// Functions for creating and interacting with a generic command/request agent with error
    /// handling used to serialise posted commands and requests which are defined by asynchronous
    /// workflows.
    module CommandRequestAgent =

        /// Returns a string describing the message.
        let private messageDescription = function
            | Command       (description, _, _) 
            | ValueRequest  (description, _, _)
            | ObjectRequest (description, _, _) -> description
            | Close _                           -> "Close"

        /// Asynchronously starts a new command/request agent with the given name (function of
        /// the handle, used for logging purposes) and initialisation workflow. The initialisation
        /// may fail, in which case this workflow will return failure. Furthermore, the processing
        /// of any given message may fail.
        let create<'Handle> (nameFunc : 'Handle -> string) (init : unit -> Async<Choice<'Handle, exn>>) = async { 
            let! initResult = init () // perform initialisation
            let handle = Choice.bindOrRaise initResult
            return CommandRequestAgent // if it succeeds, start the mailbox processing loop
            <| Agent.Start(fun (mailbox : Agent<CommandRequestMessage<'Handle>>) ->
                let log = LogManager.GetLogger (nameFunc handle) // create a logger
                
                let logResponse description = function
                    | Success s           -> sprintf "Successfully responded to message \"%s\" with %A." description s   |> log.Debug
                    | Failure (exn : exn) -> sprintf "Failed to respond to message \"%s\": %A" description (exn.Message) |> log.Error
            
                /// Workflow performed when shutting down the agent.
                let closeAgent closeFunc (replyChannel : AsyncReplyChannel<Choice<unit, exn>>) = async {
                    "Closing agent." |> log.Info
                    let! response = closeFunc handle
                    response |> replyChannel.Reply
                    logResponse "Close" response }

                /// Default message-processing loop.
                let rec loop handle = async {
                    let! message = mailbox.Receive()
                    sprintf "Received message: %s." (messageDescription message) |> log.Debug

                    match message with 
                    | Command (_, commandFunc, replyChannel) ->
                        let! response = commandFunc handle
                        response |> replyChannel.Reply
                        logResponse (messageDescription message) response
                        return! loop handle

                    | ValueRequest (_, requestFunc, replyChannel) ->
                        let! response = requestFunc handle
                        response |> replyChannel.Reply
                        logResponse (messageDescription message) response
                        return! loop handle

                    | ObjectRequest (_, requestFunc, replyChannel) ->
                        let! response = requestFunc handle
                        response |> replyChannel.Reply
                        logResponse (messageDescription message) response
                        return! loop handle
            
                    | Close (closeFunc, replyChannel) ->
                        // stop looping once the close message is received
                        do! closeAgent closeFunc replyChannel }
                
                loop handle) }

        /// Posts a command to the message queue which will be executed by performing the given
        /// async workflow with the agent handle. If failure occurs, the enclosed exception is
        /// raised. The provided description is used for logging.
        let performCommandAsync description commandBuilder (CommandRequestAgent agent) = async {
            let! response = 
                fun replyChannel -> Command(description, commandBuilder, replyChannel)
                |> agent.PostAndAsyncReply 

            return Choice.bindOrRaise response }

        /// Posts a command to the message queue which will be executed by calling the provided
        /// function with the agent handle. If failure occurs, the enclosed exception is raised.
        /// The provided description is used for logging.
        let performCommand description commandFunc agent =
            let commandBuilder handle = async { return handle |> commandFunc }
            performCommandAsync description commandBuilder agent

        /// Posts a request to the message queue which will be executed by performing the given
        /// async workflow with the agent handle. If the request is successful, the object is returned.
        /// If failure occurs, the enclosed exception is raised. The provided description is used
        /// for logging.
        let performObjectRequestAsync<'Handle, 'Result when 'Result :> obj>
                description (requestBuilder : 'Handle -> Async<Choice<'Result, exn>>) (CommandRequestAgent agent) = 
            let castRequestBuilder handle = async {
                let! result = handle |> requestBuilder
                return result |> Choice.map (fun r -> r :> obj) }
                
            async {
                let! response =
                    fun replyChannel -> ObjectRequest(description, castRequestBuilder, replyChannel)
                    |> agent.PostAndAsyncReply
                return response |> Choice.map (fun result -> result :?> 'Result) |> Choice.bindOrRaise }
 
        /// Posts a request to the message queue which will be executed by calling the provided
        /// function with the agent handle. If the request is successful, the object is returned.
        /// If failure occurs, the enclosed exception is raised. The provided description is used
        /// for logging.
        let performObjectRequest<'Handle, 'Result when 'Result :> obj>
                description (requestFunc : 'Handle -> Choice<'Result, exn>) agent =
            let requestBuilder handle = async { return handle |> requestFunc }
            performObjectRequestAsync description requestBuilder agent

        /// Posts a request to the message queue which will be executed by performing the given async
        /// workflow with the agent handle. If the request is successful, the value is returned. If
        /// failure occurs, the enclosed exception is raised. The provided description is used for
        /// logging.
        let performValueRequestAsync<'Handle, 'Result when 'Result :> ValueType>
                description (requestBuilder : 'Handle -> Async<Choice<'Result, exn>>) (CommandRequestAgent agent) =
            let castRequestBuilder handle = async {
                let! r = handle |> requestBuilder
                return r |> Choice.map (fun r -> r :> ValueType) }
            
            async {
                let! response = 
                    fun replyChannel -> ValueRequest(description, castRequestBuilder, replyChannel)
                    |> agent.PostAndAsyncReply 
                
                return response |> Choice.map (fun value -> value :?> 'Result) |> Choice.bindOrRaise }
    
        /// Posts a request to the message queue which will be executed by calling the provided
        /// function with the agent handle. If the request is successful, the value is returned. If
        /// failure occurs, the enclosed exception is raised. The provided description is used for
        /// logging.
        let performValueRequest<'Handle, 'Result when 'Result :> ValueType> 
                description (requestFunc : 'Handle -> Choice<'Result, exn>) agent =
            let requestBuilder handle = async { return handle |> requestFunc }
            performValueRequestAsync description requestBuilder agent

        /// Shuts down the message-processing agent after performing the given closing async workflow with the
        /// agent handle. If failure occurs during this process, the enclosed exception is raised.
        /// An exception will also be raised if there are any remaining messages in the queue at this point.
        let closeAsync closeBuilder (CommandRequestAgent agent) = async {
            let! response = 
                fun replyChannel -> Close(closeBuilder, replyChannel)
                |> agent.PostAndAsyncReply
            
            return Choice.bindOrRaise response }
            
        /// Shuts down the message-processing agent after calling the given closing function with the
        /// agent handle. If failure occurs during this process, the enclosed exception is raised.
        /// An exception will also be raised if there are any remaining messages in the queue at this point.
        let close closeFunc agent =
            let closeBuilder handle = async { return handle |> closeFunc }
            closeAsync closeBuilder agent