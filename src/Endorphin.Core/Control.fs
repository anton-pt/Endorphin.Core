// Copyright (c) University of Warwick. All Rights Reserved. Licensed under the Apache License, Version 2.0. See LICENSE.txt in the project root for license information.

namespace Endorphin.Core

open System
open System.Threading

[<RequireQualifiedAccess; CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module Async =
    /// Apply a mapping function inside an async workflow.
    let map mapping workflow = async {
        let! result = workflow
        return mapping result }

    /// Apply a mapping function which takes two async workflows and returns a new one.
    let map2 mapping workflow1 workflow2 = async {
        let! result1 = workflow1
        let! result2 = workflow2
        return mapping result1 result2 }

[<AutoOpen>]
/// Useful extensions for asynchronous workflows.
module AsyncExtensions =

    /// Standard type alias for Microsoft.FSharp.Control.MailboxProcessor agent.
    type Agent<'T> = MailboxProcessor<'T>

    /// A single-fire result channel which can be used to await a result asynchronously.
    type ResultChannel<'T>() =               
        let mutable result = None   // result is None until one is registered
        let mutable savedConts = [] // list of continuations which will be applied to the result
        let syncRoot = new obj()    // all writes of result are protected by a lock on syncRoot

        /// Record the result, starting any registered continuations.
        member channel.RegisterResult (res : 'T) =
            let grabbedConts = // grab saved continuations and register the result
                lock syncRoot (fun () ->
                    if channel.ResultAvailable then // if a result is already saved the raise an error
                        failwith "Multiple results registered for result channel."
                    else // otherwise save the result and return the saved continuations
                        result <- Some res
                        List.rev savedConts)

            // run all the grabbed continuations with the provided result
            grabbedConts |> List.iter (fun cont -> cont res)

        /// Check if a result has been registered with the channel.
        member channel.ResultAvailable = result.IsSome

        /// Wait for a result to be registered on the channel asynchronously.
        member channel.AwaitResult () = async {
            let! ct = Async.CancellationToken // capture the current cancellation token
        
            // create a flag which indicates whether a continuation has been called (either cancellation
            // or success, and protect access under a lock; the performCont function sets the flag to true
            // if it wasn't already set and returns a boolen indicating whether a continuation should run
            let performCont = 
                let mutable continued = false
                let localSync = obj()
                (fun () ->
                    lock localSync (fun () ->
                        if not continued 
                        then continued <- true ; true
                        else false))
        
            // wait for a result to be registered or cancellation to occur asynchronously
            return! Async.FromContinuations(fun (cont, _, ccont) ->
                let resOpt = 
                    lock syncRoot (fun () ->
                        match result with
                        | Some _ -> result // if a result is already set, capture it
                        | None   ->
                            // otherwise register a cancellation continuation and add the success continuation
                            // to the saved continuations
                            let reg = ct.Register(fun () -> 
                                if performCont () then 
                                    ccont (new System.OperationCanceledException("The operation was canceled.")))
                                
                            let cont' = (fun res ->
                                // modify the continuation to first check if cancellation has already been
                                // performed and if not, also dispose the cancellation registration
                                if performCont () then
                                    reg.Dispose()
                                    cont res)
                            savedConts <- cont' :: savedConts
                            None)

                // if a result already exists, then call the result continuation outside the lock
                match resOpt with
                | Some res -> cont res
                | None     -> ()) }

    type Async<'T> with
        
        /// Asynchronously waits for the next value in an observable sequence. If an error occurs in the sequence
        /// then the error continuation is called. If the sequence finishes before another value is observed then
        /// the cancellation continuation is called.
        static member AwaitObservable (obs:IObservable<'T>) =
          async {
              let! token = Async.CancellationToken // capture the current cancellation token
              
              return! Async.FromContinuations(fun (cont, econt, ccont) ->
                  Async.Start <| async { 
                      // create a result channel to capture the result when one occurs
                      let resultChannel = new ResultChannel<_>()
                      let resultRegistered = ref false
                      
                      // define a helper function to ensure that only one result is posted to the channel
                      let registerResult r =
                          lock resultChannel (fun () -> 
                              if not !resultRegistered then
                                  resultChannel.RegisterResult r
                                  resultRegistered := true)

                      // register a callback with the cancellation token which posts a cancellation message
                      use __ = token.Register(fun _ ->
                          registerResult (Choice3Of3 (new OperationCanceledException("The operation was cancelled."))))

                      // subscribe to the observable and post the appropriate result to the result channel
                      // when the next notification occurs
                      use __ = 
                          obs.Subscribe ({ new IObserver<'T> with
                              member __.OnNext x = registerResult (Choice1Of3 x)
                              member __.OnError exn = registerResult (Choice2Of3 exn)
                              member __.OnCompleted () = 
                                  let msg = "Cancelling workflow, because the observable awaited using AwaitObservable has completed."
                                  registerResult (Choice3Of3 (new OperationCanceledException(msg))) })
              
                      // wait for the first of these messages and call the appropriate continuation function
                      let! result = resultChannel.AwaitResult()
                      match result with
                      | Choice1Of3 x   -> cont x
                      | Choice2Of3 exn -> econt exn
                      | Choice3Of3 exn -> ccont exn }) }

    /// Type alias for System.Threading.CancellationTokenSource.
    type CancellationCapability = CancellationTokenSource

    /// Cancellation capability with generic options which must be specified at cancellation.
    type CancellationCapability<'T>() =
        let cancellationCapability = new CancellationCapability()
        let cancellationOptions = ref None

        /// Cancels the cancellation capability after setting the specified cancellation options. Can only be
        /// called once.
        member __.Cancel options =
            match !cancellationOptions with
            | None ->
                cancellationOptions := Some options
                cancellationCapability.Cancel()
            | Some _ ->
                failwith "Cancellation has already been requested."

        /// Returns the options argument specifed at cancellation. Throws an exception if cancellation has not
        /// taken place.
        member __.Options : 'T =
            match !cancellationOptions with
            | Some options -> options
            | None -> failwith "Cannot obtain cancellation options as cancellation has not yet been requested."

        /// Checks if cancellation has already been requested.
        member __.IsCancellationRequested =
            cancellationCapability.IsCancellationRequested

        /// Returns the cancellation token.
        member __.Token =
            cancellationCapability.Token

        interface IDisposable with
            /// Disposes of the System.Threading.CancellationCapability object.
            member __.Dispose() =
                cancellationCapability.Dispose()