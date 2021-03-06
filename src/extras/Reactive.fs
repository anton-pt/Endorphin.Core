// Copyright (c) University of Warwick. All Rights Reserved. Licensed under the Apache License, Version 2.0. See LICENSE.txt in the project root for license information.

namespace Endorphin.Core

open System
open System.Reactive.Linq
open FSharp.Control.Reactive

/// Helper functions for manipulating creating and manipulating a simple ring buffer.
module private RingBuffer =
    type RingBuffer<'T> =   
        { mutable StartIndex : int
          mutable FillCount  : int
          Data               : 'T array }

    /// Creates an empty ring buffer of the specified length.
    let create length =
        { StartIndex = 0
          FillCount = 0
          Data = Array.zeroCreate length }
    
    /// Returns the first index of the first element in the ring buffer.
    let private startIndex buffer = buffer.StartIndex

    /// Returns the number of elements currently stored in the ring buffer.
    let fillCount buffer = buffer.FillCount

    /// Returns the length of the buffer.
    let length buffer = buffer.Data.Length

    /// Indicates whether the buffer is full or not.
    let private isFilled buffer = (fillCount buffer = length buffer)

    /// Enumerates the elements currently in the buffer.
    let enumerate buffer = seq { for i in 0 .. fillCount buffer - 1 -> buffer.Data.[(startIndex buffer + i) % length buffer] }

    /// Pushes the element to the buffer by mutating it and returns the buffer
    let push x buffer =
        buffer.Data.[(startIndex buffer + fillCount buffer) % (length buffer)] <- x
        buffer.StartIndex <- 
            if isFilled buffer
            then startIndex buffer + 1
            else startIndex buffer
        buffer.FillCount  <-
            if isFilled buffer
            then fillCount buffer
            else fillCount buffer + 1
        buffer

[<RequireQualifiedAccess; CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module internal Event =

    /// Builds a new event whose elements are the result of applying the given mapping to each element
    /// in the source event and its zero-based integer index.
    let mapi mapping =
        Event.scan (fun (i, _) x -> (i + 1, mapping i x)) (0, Unchecked.defaultof<_>)
        >> Event.map snd

    /// Builds a new event which fires on the first occurrence of the given event and every n-th
    /// thereafter.
    let decimate n =
        mapi (fun i x -> (i % n = 0, x))
        >> Event.choose (fun (isNth, x) -> if isNth then Some x else None)

    /// Build a new event, collecting the elements of the source event into a buffer of the specified
    /// size. At each occurrence, the output event is the contents of the buffer which contains the
    /// most recent elements up to its specified size.
    let ringBuffer size =
        Event.scan (fun buffer x -> RingBuffer.push x buffer) (RingBuffer.create size)
        >> Event.map (fun buffer -> RingBuffer.enumerate buffer)

    /// Build a new event, applying the given mapping to each element in the source event, then
    /// collecting the values into a buffer of the specified size. At each occurrence, the output
    /// event is the contents of the buffer which contains the most recent values up to its
    /// specified size.
    let ringBufferMap size mapping =
        Event.scan (fun buffer x -> RingBuffer.push (mapping x) buffer) (RingBuffer.create size)
        >> Event.map (fun buffer -> RingBuffer.enumerate buffer)

    /// Build a new event, applying the given mapping to each element in the source event and its zero-
    /// based integer index, then collecting the values into a buffer of the specified size. At each
    /// occurrence, the output event is the contents of the buffer which contains the most recent values
    /// up to its specified size.
    let ringBufferMapi size mapping =
        Event.scan (fun (i, buffer) x -> (i + 1, RingBuffer.push (mapping i x) buffer)) (0, RingBuffer.create size)
        >> Event.map (fun (_, buffer) -> RingBuffer.enumerate buffer)

[<AutoOpen>]
module internal ObservableHelpers =
    
    /// Cases of an event which has completion and error semantics like an Observable. 
    type Notification<'T> =
        | Completed
        | Next of 'T
        | Error of exn
        
    type NotificationEvent<'T> = Event<Notification<'T>>

[<RequireQualifiedAccess; CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module internal Observable =

    /// Builds a new event which fires on the first occurrence of the given event and  every n-th
    /// thereafter.
    let decimate n =
        Observable.mapi (fun i x -> (i % n = 0, x))
        >> Observable.choose (fun (isNth, x) -> if isNth then Some x else None)

    /// Build a new observable, collecting the elements of the source observable into a buffer of the
    /// specified size. At each occurrence, the output observable  is the contents of the buffer which
    /// contains the most recent elements up to its specified size.
    let ringBuffer size =
        Observable.scanInit (RingBuffer.create size) (fun buffer x -> RingBuffer.push x buffer)
        >> Observable.map (fun buffer -> RingBuffer.enumerate buffer)

    /// Build a new observable, applying the given mapping to each element in the source observable,
    /// then collecting the values into a buffer of the specified size. At each occurrence, the output
    /// observable is the contents of the buffer which contains the most recent values up to its
    /// specified size.
    let ringBufferMap size mapping =
        Observable.scanInit (RingBuffer.create size) (fun buffer x -> RingBuffer.push (mapping x) buffer)
        >> Observable.map (fun buffer -> RingBuffer.enumerate buffer)

    /// Build a new observable, applying the given mapping to each element in the source observable and
    /// its zero-based integer index, then collecting the values into a buffer of the specified size. At
    /// each occurrence, the output event is the contents of the buffer which contains the most recent
    /// values up to its specified size.
    let ringBufferMapi size mapping =
        Observable.scanInit (0, RingBuffer.create size) (fun (i, buffer) x -> (i + 1, RingBuffer.push (mapping i x) buffer))
        >> Observable.map (fun (_, buffer) -> RingBuffer.enumerate buffer)

    /// Creates an observable from a source event which emits notifications that either hold a value,
    /// completion or error.
    let fromNotificationEvent (source : IEvent<Notification<'T>>) =
        Observable.Create(fun (observer : IObserver<_>) ->
            source.Subscribe(fun notification ->
                match notification with
                | Notification.Next value -> observer.OnNext value
                | Notification.Completed  -> observer.OnCompleted()
                | Notification.Error exn  -> observer.OnError exn))