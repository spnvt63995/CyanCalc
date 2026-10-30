# File compatibility fixtures

Synthetic workbooks serialized by IronCalc engine `8f8c0f8f294387dcf53580148371de26d41002f5`.
The JSON records expected cell contents and IC/XLSX hashes.
The Rust compatibility test checks that an unsupported `.ic` schema is rejected without
changing the current model, while the XLSX representation can be imported.
The files contain example numbers, formulas, names, links, and formatting.
