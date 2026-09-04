import ghidra.app.script.GhidraScript;
import ghidra.program.model.listing.*;
import java.io.*;

public class DumpSyms extends GhidraScript {
    @Override
    public void run() throws Exception {
        FunctionManager fm = currentProgram.getFunctionManager();
        long base = currentProgram.getImageBase().getOffset();
        File out = new File("E:/modgames/bdc/mod/research/funcs.txt");
        PrintWriter w = new PrintWriter(new OutputStreamWriter(new FileOutputStream(out), "UTF-8"));
        int n = 0;
        for (Function f : fm.getFunctions(true)) {
            long ea = f.getEntryPoint().getOffset();
            w.printf("%08x\t%s%n", ea - base, f.getName(true));
            n++;
        }
        w.close();
        println("WROTE " + n + " functions");
    }
}
