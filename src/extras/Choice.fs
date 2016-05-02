// Copyright (c) University of Warwick. All Rights Reserved. Licensed under the Apache License, Version 2.0. See LICENSE.txt in the project root for license information.

namespace Endorphin.Core

open System

[<RequireQualifiedAccess>]
/// Utility functions for error handling where the success or failure of a result is represented using the
/// Choice type.
module internal Choice =

    /// Return a success state of the given value.
    let succeed = Choice1Of2

    /// Return a failure state of the given value.
    let fail = Choice2Of2

    /// Map a function onto a choice type.  If the choice is in the success state, then apply the mapping,
    /// otherwise return the previous failure message.
    let map (mapping : 'T -> 'U) (state : Choice<'T, 'Error>) =
        match state with
        | Choice1Of2 s -> succeed <| mapping s
        | Choice2Of2 f -> fail f

    /// Map a function onto the error type of a choice state.  If the choice is in the error state, then
    /// apply the mapping.  Otherwise simply return the success state.
    let mapError (mapping : 'T -> 'U) (state : Choice<'Success, 'T>) =
        match state with
        | Choice1Of2 s -> succeed s
        | Choice2Of2 f -> fail <| mapping f

    /// Bind a choice-returning function onto an existing choice.  If the given state is in the success
    /// state, then apply the binding to the successful value.  If the given state is in the failure state,
    /// then simply propagate that failure.
    let bind (binder : 'T -> Choice<'U, 'Error>) state =
        match state with
        | Choice1Of2 s -> binder s
        | Choice2Of2 f -> fail f

    /// Return the success value of a state, or raise the exception stored in the error state.
    let bindOrRaise (state : Choice<'T, #exn>) =
        match state with
        | Choice1Of2 s -> s
        | Choice2Of2 f -> raise f

    /// Lift any value into being a Choice in the success state.
    let lift value : Choice<'T, 'Error> = succeed value

    [<Sealed>]
    type Builder () =

        /// The default case for the empty computation expression is a success with no value.
        static let zero = Choice1Of2 ()

        /// Bind a new choice onto the existing state.
        member __.Bind (state : Choice<'T, 'Error>, binder) : Choice<'U, 'Error> =
            bind binder state

        /// Return a value as a choice.
        member __.Return value : Choice<'T, 'Error> = lift value

        /// Return a choice value from a computational expression.
        member __.ReturnFrom state : Choice<'T, 'Error> = state

        /// Get the zero value of the choice computational expression.
        member __.Zero () : Choice<unit, 'Error> = zero

        /// Delay performing a function inside a choice computational expression.
        member __.Delay generator : Choice<'T, 'Error> = generator ()

        /// Combine a value with the global state. The input state must be Choice<unit, _> for this
        /// to work.
        member __.Combine (state, value) : Choice<'T, 'Error> =
            match state with
            | Choice1Of2 () -> value
            | Choice2Of2 f  -> fail f

[<AutoOpen>]
/// Success/Failure semantics for Choice<'T1, 'T2> and Choice.Builder instance available directly in the
/// Endorphin.Core namespace.
module internal ChoiceHelpers =
    /// Success or failure semantics for Choice types.  These function simply adds aliases for Choice1Of2
    /// and Choice2Of2 corresponding to Success and Failure.
    let (|Success|Failure|) = function
        | Choice1Of2 s -> Success s
        | Choice2Of2 f -> Failure f
    
    /// Builds computations which yield a Choice representing success or failure.
    let choice = new Choice.Builder ()