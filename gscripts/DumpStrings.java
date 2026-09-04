import ghidra.app.script.GhidraScript;
import ghidra.program.model.listing.*;
import java.io.*;

public class DumpStrings extends GhidraScript {
    @Override
    public void run() throws Exception {
        long base = currentProgram.getImageBase().getOffset();
        PrintWriter w = new PrintWriter(new OutputStreamWriter(
            new FileOutputStream("E:/modgames/bdc/mod/research/strings.txt"), "UTF-8"));
        int n = 0;
        DataIterator it = currentProgram.getListing().getDefinedData(true);
        while (it.hasNext()) {
            Data d = it.next();
            Object v = d.getValue();
            if (!(v instanceof String)) continue;
            String s = ((String) v).replace("\n", "\n").replace("\r", "\r");
            if (s.length() < 3) continue;
            w.printf("%08x\t%s%n", d.getAddress().getOffset() - base, s);
            n++;
        }
        w.close();
        println("WROTE " + n + " strings");
    }
}
