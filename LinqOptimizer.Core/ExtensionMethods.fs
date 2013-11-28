﻿    
namespace LinqOptimizer.Core
    
    open System
    open System.Collections
    open System.Collections.Generic
    open System.Linq
    open System.Linq.Expressions
    open System.Reflection
    open System.Reflection.Emit


    type CoreHelpers =
        static member AsQueryExpr(enumerable : IEnumerable, ty : Type) : QueryExpr = 
            // Hack to optimize Enumerable.Range and Enumerable.Repeat calls
            // TODO : check Mono generated types
            let t = enumerable.GetType()
            match t.FullName with
            | s when s.StartsWith "System.Linq.Enumerable+<RangeIterator>"  ->
                let start = t.GetFields().First(fun f -> f.Name.EndsWith "__start").GetValue(enumerable)
                let count = t.GetFields().First(fun f -> f.Name.EndsWith "__count").GetValue(enumerable)
                RangeGenerator(constant start , constant count)
            | s when s.StartsWith "System.Linq.Enumerable+<RepeatIterator>"  ->
                let element = t.GetFields().First(fun f -> f.Name.EndsWith "__element").GetValue(enumerable)
                let count   = t.GetFields().First(fun f -> f.Name.EndsWith "__count").GetValue(enumerable)
                RepeatGenerator(Expression.Convert(constant element, ty) , constant count)
            | _ -> 
                Source (constant enumerable, ty)

#if CORE_COMPILETOMETHOD
        static member private Compile(query : QueryExpr, compile : QueryExpr -> Expression) : MethodInfo * obj [] =
            let expr = compile query
            
            let eraser = AnonymousTypeEraser()
            let expr = eraser.Visit(expr)
            
            let csv = ConstantLiftingTransformer()
            let expr' = csv.Visit(expr)
            let objs, pms = csv.Environment.Values.ToArray(), csv.Environment.Keys

            let func = Expression.Lambda(expr', pms)
            Session.Compile(func), objs

        static member private WrapInvocation<'T>(mi : MethodInfo, args : obj []) =
            fun () -> 
                try mi.Invoke(null, args) :?> 'T
                with :? TargetInvocationException as ex -> raise ex.InnerException 

        static member Compile<'T>(queryExpr : QueryExpr, optimize : Func<Expression,Expression>) : Func<'T> =
            let mi, objs = CoreHelpers.Compile(queryExpr, fun expr -> Compiler.compileToSequential expr optimize.Invoke )
            Func<'T>(CoreHelpers.WrapInvocation(mi, objs))

        static member Compile(queryExpr : QueryExpr, optimize : Func<Expression,Expression>) : Action =
            let mi, objs = CoreHelpers.Compile(queryExpr, fun expr -> Compiler.compileToSequential expr optimize.Invoke )
            Action(CoreHelpers.WrapInvocation(mi, objs))

        static member CompileToParallel<'T>(queryExpr : QueryExpr,  optimize : Func<Expression,Expression>) : Func<'T> =
            let mi, objs = CoreHelpers.Compile(queryExpr,  fun expr -> Compiler.compileToParallel expr optimize.Invoke )
            Func<'T>(CoreHelpers.WrapInvocation(mi, objs))
#else
        static member Compile<'T>(queryExpr : QueryExpr, optimize : Func<Expression,Expression>) : Func<'T> =
            eprintfn "Warning : Compiling without CompileToMethod. Possible degradation of performance."
            let expr = Compiler.compileToSequential queryExpr optimize.Invoke
            Func<'T>(fun () -> Expression.Lambda(expr).Compile().DynamicInvoke() :?> 'T)

        static member Compile(queryExpr : QueryExpr, optimize : Func<Expression,Expression>) : Action =
            eprintfn "Warning : Compiling without CompileToMethod. Possible degradation of performance."
            let expr = Compiler.compileToSequential queryExpr optimize.Invoke
            Action(fun () -> Expression.Lambda(expr).Compile().DynamicInvoke() :?> unit)

        static member CompileToParallel<'T>(queryExpr : QueryExpr,  optimize : Func<Expression,Expression>) : Func<'T> =
            eprintfn "Warning : Compiling without CompileToMethod. Possible degradation of performance."
            let expr = Compiler.compileToParallel queryExpr optimize.Invoke
            Func<'T>(fun () -> Expression.Lambda(expr).Compile().DynamicInvoke() :?> 'T)
#endif
