#ifndef INC_TEST_H_
#define INC_TEST_H_

#ifdef __unix__
#define TEST1_API
#else
#ifdef test1_EXPORTS
#define TEST1_API __declspec(dllexport)
#else
#define TEST1_API __declspec(dllimport)
#endif
#endif

#ifdef __cplusplus
extern "C" {
#endif
TEST1_API void __stdcall print_hello(void);
#ifdef __cplusplus
}
#endif
#endif
