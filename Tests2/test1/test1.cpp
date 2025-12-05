#include "test1.h"
#include <stdio.h>
#ifdef __cplusplus
extern "C" {
#endif
TEST1_API void __stdcall print_hello(void)
{
    printf("Hello World\n");
}
#ifdef __cplusplus
}
#endif

