using Aspose.Pdf.Core;

namespace Aspose.Pdf.Content;

/// <summary>
/// Something that may take an instruction of a content stream over.
///
/// A content stream is a list of instructions, each preceded by the values it
/// operates on. Reading it — finding where one instruction ends and the next
/// begins, turning the values into objects, knowing that a string may be
/// bracketed or hexadecimal and that an array may hold either — is the same work
/// whoever consumes the result. What each instruction MEANS is not: a caller
/// with its own model of a page, or one implementing the instruction set of
/// another library, has its own answer and cannot use ours.
///
/// So this splits the two. The parser reads, and offers each instruction here
/// before acting on it; a handler that answers yes has dealt with it and the
/// parser does nothing further with it.
///
/// ⚠ The instruction is passed apart from its operands. A caller whose own
/// convention puts the instruction at the end of the operand list — several do,
/// because it is the order the file states them in — appends it itself.
/// </summary>
/// <remarks>
/// Internal because the objects it hands over are: this library's PDF object
/// model is internal.
/// </remarks>
internal interface IInstructionHandler
{
    /// <summary>
    /// Offered one instruction and the values before it.
    ///
    /// Answer <c>true</c> to say it has been handled, which stops the parser
    /// acting on it; <c>false</c> to let the parser carry on as though nothing
    /// were listening. Answering yes to some instructions and no to others is
    /// the point: a caller that only cares about text can take the text ones
    /// and leave the rest to keep the graphics state honest.
    /// </summary>
    /// <param name="instruction">The operator, as the file spells it: <c>re</c>,
    /// <c>Tj</c>, <c>cm</c>. Never empty.</param>
    /// <param name="operands">The values stated before it, in that order, and
    /// WITHOUT the instruction itself. Empty where it takes none. Valid only for
    /// the length of the call — the parser reuses the list.</param>
    /// <param name="state">The graphics state as it stands, before this
    /// instruction has been allowed to change it.</param>
    bool Handled(string instruction, IReadOnlyList<PdfObject> operands, GraphicsState state);
}
