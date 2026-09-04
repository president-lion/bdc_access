import ghidra.app.script.GhidraScript;
import ghidra.app.decompiler.*;
import ghidra.program.model.address.Address;
import ghidra.program.model.listing.*;
import ghidra.program.model.symbol.Reference;
import java.io.*;
import java.util.*;

public class StrRefs extends GhidraScript {
    @Override
    public void run() throws Exception {
        String[] args = getScriptArgs();
        String outPath = args[0];
        Set<String> needles = new HashSet<>();
        for (int i = 1; i < args.length; i++) needles.add(args[i]);

        long base = currentProgram.getImageBase().getOffset();
        DecompInterface di = new DecompInterface();
        di.openProgram(currentProgram);
        PrintWriter w = new PrintWriter(new OutputStreamWriter(new FileOutputStream(outPath), "UTF-8"));
        Set<Function> seen = new HashSet<>();

        DataIterator it = currentProgram.getListing().getDefinedData(true);
        while (it.hasNext()) {
            Data d = it.next();
            Object v = d.getValue();
            if (!(v instanceof String)) continue;
            if (!needles.contains((String) v)) continue;
            w.printf("%nSTRING %08x  \"%s\"%n", d.getAddress().getOffset() - base, v);
            for (Reference r : getReferencesTo(d.getAddress())) {
                Function f = getFunctionContaining(r.getFromAddress());
                long from = r.getFromAddress().getOffset() - base;
                if (f == null) { w.printf("  xref %08x <no func>%n", from); continue; }
                w.printf("  xref %08x in func %08x (%s)%n", from,
                         f.getEntryPoint().getOffset() - base, f.getName());
            }
        }
        w.close();
        di.dispose();
        println("STRREFS DONE -> " + outPath);
    }
}
