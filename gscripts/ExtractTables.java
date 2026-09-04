import ghidra.app.script.GhidraScript;
import ghidra.program.model.address.Address;
import ghidra.program.model.listing.*;
import ghidra.program.model.symbol.Reference;
import ghidra.program.model.symbol.RefType;
import java.io.*;
import java.util.*;

/**
 * Walks every call site of a GameMaker registrar and recovers the constant PUSHes
 * that precede it, which are the registration's arguments (cdecl, right-to-left).
 */
public class ExtractTables extends GhidraScript {

    private String readCStr(long va) {
        try {
            Address a = toAddr(va);
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < 128; i++) {
                byte b = getByte(a.add(i));
                if (b == 0) break;
                if (b < 32 || b > 126) return null;
                sb.append((char) b);
            }
            return sb.length() == 0 ? null : sb.toString();
        } catch (Exception e) { return null; }
    }

    private void dump(long registrar, int nargs, String outPath, String header) throws Exception {
        PrintWriter w = new PrintWriter(new OutputStreamWriter(new FileOutputStream(outPath), "UTF-8"));
        w.println(header);
        Address reg = toAddr(registrar);
        List<Reference> refs = new ArrayList<>();
        for (Reference r : getReferencesTo(reg))
            if (r.getReferenceType().isCall()) refs.add(r);
        refs.sort(Comparator.comparing(Reference::getFromAddress));

        int n = 0;
        for (Reference r : refs) {
            Instruction call = getInstructionAt(r.getFromAddress());
            if (call == null) continue;
            // collect the last `nargs` constant pushes before the call
            LinkedList<Long> pushes = new LinkedList<>();
            Instruction cur = call.getPrevious();
            int guard = 0;
            while (cur != null && pushes.size() < nargs && guard++ < 24) {
                if (cur.getMnemonicString().equals("PUSH") && cur.getNumOperands() == 1) {
                    Object[] o = cur.getOpObjects(0);
                    if (o.length == 1 && o[0] instanceof ghidra.program.model.scalar.Scalar) {
                        pushes.addFirst(((ghidra.program.model.scalar.Scalar) o[0]).getUnsignedValue());
                    } else break;
                } else if (cur.getMnemonicString().equals("CALL")) break;
                cur = cur.getPrevious();
            }
            if (pushes.size() < nargs) continue;
            // cdecl: pushes are reverse order -> first arg pushed last
            Collections.reverse(pushes);
            String name = readCStr(pushes.get(0));
            if (name == null) continue;
            StringBuilder sb = new StringBuilder();
            sb.append(String.format("%-40s", name));
            for (int i = 1; i < nargs; i++) sb.append(String.format("\t%08x", pushes.get(i)));
            sb.append(String.format("\t; site %08x", r.getFromAddress().getOffset()));
            w.println(sb);
            n++;
        }
        w.close();
        println("WROTE " + n + " -> " + outPath);
    }

    @Override
    public void run() throws Exception {
        // Code_Function_Add(name, func, minargs, maxargs)
        dump(0x00526bf0L, 4, "E:/modgames/bdc/mod/research/table_functions.txt",
             "name\tfunc\tminargs\tmaxargs");
        // Variable_BuiltIn_Add(name, getter, setter, ?)
        dump(0x0040d050L, 3, "E:/modgames/bdc/mod/research/table_variables.txt",
             "name\tgetter\tsetter");
    }
}
